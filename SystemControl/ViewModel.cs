using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using SystemControl.Models;
using SystemControl.Utils;

namespace SystemControl
{
    public class ViewModel : NotificationObject
    {
        private readonly ConcurrentQueue<DirectoryInfo> _directories = new ConcurrentQueue<DirectoryInfo>(); //Очередь для потоков

        private readonly ConcurrentBag<string> _censoredWordsBag = new ConcurrentBag<string>();

        private readonly ObservableCollection<string> _processedFilesConsoleItems = new ObservableCollection<string>();
        private readonly ObservableCollection<ReportFileEntry> _filesWithCensoredWords = new ObservableCollection<ReportFileEntry>();
        private readonly ObservableCollection<CensoredWord> _censoredWords = new ObservableCollection<CensoredWord>();

        private State _state = State.Idle;

        private Timer _updateTimer;
        private bool _directoiesSearchStarted = false;

        private int _totalDirectoriesCount = 0;
        private int _currentDirectoriesCount = 0;
        private int _analysedFilesCount = 0;

        private DirectoryInfo _analyseFolder;
        private DirectoryInfo _reportsFolder;
        private DirectoryInfo _currentReportFolder;

        private string _currentReportId;

        private List<string> _excludeFolders = new List<string>();

        public ViewModel(IConfiguration configuration)
        {
            _updateTimer = new Timer(Update);

            ThreadPool.SetMaxThreads(300, 200);

            var excludeFolders = configuration.GetValue<string>("excludeFoldersWords");
            if (!string.IsNullOrEmpty(excludeFolders))
            {
                _excludeFolders.AddRange(excludeFolders.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim().ToLower()));
            }

            var analyseFolder = configuration.GetValue<string>("analyseFolder");
            if (!string.IsNullOrEmpty(analyseFolder))
            {
                var directory = new DirectoryInfo(analyseFolder);
                if (directory.Exists)
                {
                    _analyseFolder = directory;
                }
            }

            var reportsFolder = configuration.GetValue<string>("reportsFolder");
            if (!string.IsNullOrEmpty(reportsFolder))
            {
                try
                {

                    var directory = new DirectoryInfo(reportsFolder);
                    if (!directory.Exists)
                    {
                        directory.Create();
                    }

                    _reportsFolder = directory;

                }
                catch (Exception ex)
                {
                    State = State.BadConfig;
                    MessageBox.Show(reportsFolder, ex.Message);
                }
            }

            var censoredWords = configuration.GetValue<string>("censoredWords");
            if (!string.IsNullOrEmpty(censoredWords))
            {
                foreach (var word in censoredWords.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()))
                {
                    _censoredWords.Add(new CensoredWord(word));
                    _censoredWordsBag.Add(word);
                }
            }
            else
            {
                State = State.BadConfig;
                MessageBox.Show("Не найдены настройки запрещённых слов!");
            }

            if (configuration.GetValue<bool>("autostart") || configuration.GetValue<bool>("hidden"))
                Start();
        }

        public ObservableCollection<string> ProcessedFilesConsole => _processedFilesConsoleItems;
        public ObservableCollection<ReportFileEntry> FilesWithCensoredWords => _filesWithCensoredWords;
        public ObservableCollection<CensoredWord> CensoredWords => _censoredWords;

        public State State
        {
            get { return _state; }
            private set
            {
                _state = value;
                NotifyPropertyChanged();
            }
        }

        public int TotalDirectoriesCount
        {
            get { return _totalDirectoriesCount; }
            set
            {
                _totalDirectoriesCount = value;
                NotifyPropertyChanged();
            }
        }

        public int CurrentDirectoriesCount
        {
            get { return _currentDirectoriesCount; }
            set
            {
                _currentDirectoriesCount = value;
                NotifyPropertyChanged();
            }
        }

        public int AnalysedFilesCount
        {
            get { return _analysedFilesCount; }
            set
            {
                _analysedFilesCount = value;
                NotifyPropertyChanged();
            }
        }

        public void Start()
        {
            if (State == State.BadConfig)
                return;

            if (State != State.Paused) //папка для отчета
            {
                _currentReportId = DateTime.Now.ToString("ddMMyyyyHHmmss");

                _currentReportFolder = _reportsFolder.CreateSubdirectory(_currentReportId);
                _currentReportFolder.CreateSubdirectory("OriginalFiles");
                _currentReportFolder.CreateSubdirectory("CensoredFiles");
            }

            if (State == State.Completed) //очищаем представление при новом запуске
            {
                TotalDirectoriesCount = CurrentDirectoriesCount = AnalysedFilesCount = 0;
                _processedFilesConsoleItems.Clear();
                _filesWithCensoredWords.Clear();

                foreach (var word in _censoredWords)
                {
                    word.Count = 0;
                }
                _directoiesSearchStarted = false;

                _directories.Clear();
            }

            State = State.Started;

            if (_directories.Count == 0)
            {
                if (ThreadPool.QueueUserWorkItem(SearchDirectories))
                {
                    _directoiesSearchStarted = true;
                }
            }

            _updateTimer.Change(0, 2);
        }
        public void Pause()
        {
            State = State.Paused;

            _updateTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void Stop()
        {
            State = State.Idle;

            _updateTimer.Change(Timeout.Infinite, Timeout.Infinite);

            _currentReportFolder.Delete(true);
            TotalDirectoriesCount = CurrentDirectoriesCount = AnalysedFilesCount = 0;
            _processedFilesConsoleItems.Clear();
            _filesWithCensoredWords.Clear();

            foreach (var word in _censoredWords)
            {
                word.Count = 0;
            }
            _directoiesSearchStarted = false;

            _directories.Clear();
        }

        private void Update(object state)
        {
            try
            {
                if (State == State.BadConfig)
                {
                    _updateTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    return;
                }

                if (State == State.Started)
                {

                    if (!_directoiesSearchStarted)
                    {
                        if (ThreadPool.QueueUserWorkItem(SearchDirectories))
                        {
                            _directoiesSearchStarted = true;
                        }
                    }

                    if (AnalysedFilesCount > 0 && _directories.Count == 0)
                    {
                        Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            State = State.Completed;

                            GenerateReportFile();
                        }, DispatcherPriority.Send);
                    }

                    if (_directories.TryPeek(out var directory))
                    {
                        if (ThreadPool.QueueUserWorkItem(AnalyseDirectory, directory))
                            _directories.TryDequeue(out directory);
                    }
                }
            }
            catch (Exception)
            { }
        }

        private void SearchDirectories(object state)
        {
            if (!IsActiveThread())
                return;

            try
            {
                if (_analyseFolder != null)
                {
                    AddDirectory(_analyseFolder);
                }
                else
                {
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        if (!IsActiveThread())
                            return;

                        if (drive.IsReady)
                        {
                            AddDirectory(drive.RootDirectory);
                        }
                    }
                }

            }
            catch (Exception)
            {
                _directoiesSearchStarted = false;
            }
        }

        private void AddDirectory(DirectoryInfo directory)
        {
            try
            {
                if (!IsActiveThread())
                    return;

                if (_excludeFolders.Any(f => directory.FullName.ToLower().Contains(f)))
                    return;

                _directories.Enqueue(directory);

                foreach (var dir in directory.EnumerateDirectories())
                {
                    if (!IsActiveThread())
                        return;

                    try
                    {
                        AddDirectory(dir);

                        AddedDirectoryDisplay(dir);
                    }
                    catch (Exception)
                    { }
                }

            }
            catch (Exception)
            { }
        }

        private void AnalyseDirectory(object state)
        {
            if (!IsActiveThread())
                return;

            try
            {
                var directory = (DirectoryInfo)state;

                foreach (var file in directory.EnumerateFiles())
                {
                    if (!IsActiveThread())
                        return;

                    try
                    {
                        AnalyseFile(file);
                    }
                    catch (Exception)
                    { }
                }
            }
            catch (Exception)
            { }
        }

        private void AnalyseFile(FileInfo fileInfo)
        {
            if (!IsActiveThread())
                return;

            if (!IsBinary(fileInfo))
            {
                bool found = false;
                var reportFile = new ReportFileEntry();
                reportFile.Name = fileInfo.Name;
                reportFile.Size = fileInfo.Length;
                reportFile.Path = fileInfo.FullName;
                reportFile.File = fileInfo;

                var fileContent = File.ReadAllText(fileInfo.FullName);

                foreach (var censoredWord in _censoredWordsBag)
                {
                    if (!IsActiveThread())
                        return;

                    if (fileContent.Contains(censoredWord))
                    {
                        found = true;

                        int cnt = Regex.Matches(fileContent, Regex.Escape(censoredWord)).Count;

                        reportFile.Words.Add(new CensoredWord(censoredWord, cnt));

                        fileContent = fileContent.Replace(censoredWord, "*******");

                        AddCensoredWordCount(censoredWord, cnt);
                    }
                }

                if (found)
                {
                    File.Copy(fileInfo.FullName, Path.Combine(_currentReportFolder.FullName, "OriginalFiles", fileInfo.Name), true);

                    File.WriteAllText(Path.Combine(_currentReportFolder.FullName, "CensoredFiles", fileInfo.Name), fileContent);

                    AddCensoredFile(reportFile);
                }

                AnalysedFileDisplay(fileInfo);
            }
        }

        private void AnalysedFileDisplay(FileInfo file)
        {
            var fileName = PathShortener(file.FullName);

            if (Application.Current != null)
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (State == State.Idle)
                        return;

                    Console.WriteLine(file.FullName);

                    _processedFilesConsoleItems.Insert(0, fileName);

                    AnalysedFilesCount++;
                    CurrentDirectoriesCount = Math.Max(0, TotalDirectoriesCount - _directories.Count);

                    if (_processedFilesConsoleItems.Count > 100)
                    {
                        _processedFilesConsoleItems.RemoveAt(_processedFilesConsoleItems.Count - 1);
                    }
                }, DispatcherPriority.Background);
            }
        }

        private void AddedDirectoryDisplay(DirectoryInfo dir)
        {
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (State == State.Idle)
                        return;

                    TotalDirectoriesCount++;
                }, DispatcherPriority.Background);
            }
        }

        private void AddCensoredWordCount(string word, int count)
        {
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (State == State.Idle)
                        return;

                    var censoredWord = _censoredWords.FirstOrDefault(x => x.Word == word);
                    if (censoredWord != null)
                        censoredWord.Count += count;

                }, DispatcherPriority.Background);
            }
        }

        private void AddCensoredFile(ReportFileEntry file)
        {
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (State == State.Idle)
                        return;

                    _filesWithCensoredWords.Insert(0, file);

                }, DispatcherPriority.Background);
            }
        }

        private void GenerateReportFile()
        {
            var report = new Report(_currentReportId, DateTime.Now);

            report.CensoredWords = _censoredWords.ToList();
            report.Files = _filesWithCensoredWords.ToList();

            File.WriteAllText(Path.Combine(_currentReportFolder.FullName, "report.json"), JsonConvert.SerializeObject(report, Formatting.Indented), Encoding.UTF8);

            if (App.Configuration.GetValue<bool>("hidden"))
            {
                Console.WriteLine($"Completed. Report file: {Path.Combine(_currentReportFolder.FullName, "report.json")}");

                App.Current.Shutdown();
            }
        }

        


        private bool IsActiveThread()
        {
            if (State == State.Idle)
                return false;

            if (State == State.Paused)
            {
                while (State == State.Paused)
                {
                    Thread.Sleep(1000);
                }
            }

            return true;
        }

        private string PathShortener(string path)
        {
            return path.Substring(0, 20) + "..." + path.Substring((path.Length - 20), 20);
        }

        private bool IsBinary(FileInfo fileInfo, int requiredConsecutiveNul = 1) //ищет признаки бинарных файлов (определяет, что в файле нет текста)
        {
            var filePath = fileInfo.FullName;

            const int charsToCheck = 8000;
            const char nulChar = '\0';

            int nulCount = 0;

            using (var streamReader = new StreamReader(filePath))
            {
                for (var i = 0; i < charsToCheck; i++)
                {
                    if (streamReader.EndOfStream)
                        return false;

                    if ((char)streamReader.Read() == nulChar)
                    {
                        nulCount++;

                        if (nulCount >= requiredConsecutiveNul)
                            return true;
                    }
                    else
                    {
                        nulCount = 0;
                    }
                }
            }

            return false;
        }
    }

    public enum State
    {
        Idle, //когда start еще не нажали и нажали stop
        Started,
        Paused,
        Completed,
        BadConfig 
    }
}
