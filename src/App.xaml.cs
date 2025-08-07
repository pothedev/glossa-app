using System;
using System.Threading.Tasks;
using System.Windows;
using DotNetEnv;
using Glossa.src;
using Glossa.src.utility;

namespace Glossa
{
    public partial class App : Application
    {
        //private Settings _settings;
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            

            try
            {
                Env.Load("../../../.env");

                //var settings = Settings.Load();

                var input = new InputProcessor();
                var output = new OutputProcessor();

                Task inputTask = input.Start();
                Task outputTask = output.StartContinuousListening();

                await Task.WhenAll(inputTask, outputTask);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Startup error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
