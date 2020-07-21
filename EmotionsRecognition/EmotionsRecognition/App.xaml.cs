using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace EmotionsRecognition
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            EmotionsRecognition.MainWindow window = new MainWindow();
            MainViewModel VM = new MainViewModel();
            window.DataContext = VM;
            window.Show();
        }
    }

}
