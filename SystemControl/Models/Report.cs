using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SystemControl.Models
{
    public class Report
    {
        public Report(string name, DateTime createdTime)
        {
            Name = name;
            CreatedTime = createdTime;
        }

        public string Name { get; }

        public DateTime CreatedTime { get; }

        public IEnumerable<CensoredWord> CensoredWords { get; set; }

        public IEnumerable<ReportFileEntry> Files { get; set; }
    }

    public class ReportFileEntry
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }
        public List<CensoredWord> Words { get; } = new List<CensoredWord>();

        [JsonIgnore]
        public FileInfo File { get; set; }
    }
}
