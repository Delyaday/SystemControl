using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SystemControl
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        static Mutex _mutex;
        public static IServiceProvider ServiceProvider { get; private set; }
        public static IConfiguration Configuration { get; private set; }

        [DllImport("kernel32.dll", SetLastError = true)] 
        static extern bool AttachConsole(uint dwProcessId);

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, "SystemControl", out var isNewInstance); 
            if (!isNewInstance)
            {
                Current.Shutdown();
            }

            var builder = new ConfigurationBuilder() 
              .SetBasePath(Directory.GetCurrentDirectory())
              .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
              .AddCommandLine(e.Args); 

            Configuration = builder.Build();

            var serviceCollection = new ServiceCollection(); 

            serviceCollection.AddSingleton(Configuration);
            serviceCollection.AddSingleton(typeof(ViewModel));
            serviceCollection.AddTransient(typeof(MainWindow));

            ServiceProvider = serviceCollection.BuildServiceProvider(); 

            ServiceProvider.GetRequiredService<ViewModel>();

            if (!Configuration.GetValue<bool>("hidden")) 
            {
                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }
            else
            {
                this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                //Чтобы консоль показывала процесс работы

                const uint ATTACH_PARENT_PROCESS = 0x0ffffffff;  

                AttachConsole(ATTACH_PARENT_PROCESS); 
            }
        }
    }
}
