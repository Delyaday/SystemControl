using AdonisUI.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SystemControl
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : AdonisWindow
    {
        private ViewModel _viewModel;
        public MainWindow(ViewModel viewModel)
        {
            _viewModel = viewModel;

            InitializeComponent();

            this.DataContext = _viewModel; 
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Start();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Stop();
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Pause();
        }
    }
}
