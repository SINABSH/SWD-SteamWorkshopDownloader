using System.Windows;

namespace WpfSteamDownloader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // The DataContext is the "bridge" between the View (XAML) and the ViewModel (C# logic).
            // We are telling our window that its main source of data and commands is the MainViewModel.
            DataContext = new MainViewModel();
        }

        private void DataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

        }
    }
}
