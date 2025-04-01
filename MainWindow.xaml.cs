using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Newtonsoft.Json;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics;
using Microsoft.UI.Windowing;
using Test1;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Test1
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    /// using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    public static class UdpBackgroundPinger
    {
        private static CancellationTokenSource _cts;

        public static void StartBackgroundPinging(string serverIp, int serverPort, string clientId, int intervalSeconds = 20)
        {
            _cts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                using var udpClient = new UdpClient(); // Random local port
                var serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);

                Console.WriteLine($"[PINGER] Starting UDP pings to {serverIp}:{serverPort} every {intervalSeconds} seconds...");

                try
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        byte[] message = Encoding.UTF8.GetBytes(clientId);
                        await udpClient.SendAsync(message, message.Length, serverEndPoint);
                        Console.WriteLine($"[PINGER] Ping sent at {DateTime.Now}");

                        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), _cts.Token);
                    }
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("[PINGER] Ping loop canceled.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PINGER] Error in UDP ping: {ex.Message}");
                }
            });
        }

        public static void StopPinging()
        {
            _cts?.Cancel();
            Console.WriteLine("[PINGER] Stopping background pinging...");
        }
    }

    public sealed partial class MainWindow : Window
    {

        Blockchain bc;

        private ObservableCollection<FileItem> files = new ObservableCollection<FileItem>();
        public MainWindow()
        {
            this.InitializeComponent();
            //FileListView.ItemsSource = files;
            this.Closed += MainWindow_Closed;
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            // Get the AppWindow from the handle
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);


            appWindow.Resize(new SizeInt32(1200, 775));
        }

        public class FileItem
        {
            public string Name { get; set; }
            public string Size { get; set; }
            public string Date { get; set; }
        }

        
        public async void UpdateUI(string username, string address)
        {
            string fmtadd = "@" + address;
            MainpageUsername.Text = username;
            MainpageAddress_display.Text = fmtadd;  // Display the address

            string serverIp = "4.188.232.157";
            int serverPort = 12345; // Your server UDP listener port
            string clientId = address;

            // Start background ping thread
            UdpBackgroundPinger.StartBackgroundPinging(serverIp, serverPort, clientId, 20);


        }

        public async void InitalizeBlockchain(string selfaddress)
        {

            bc = new Blockchain();
            await bc.InitializeBC(uname: MainpageUsername.ToString(), selfaddress);
        }

        private async void FabButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                FileProcessingHelper.ProcessFile(file, MainpageUsername.Text);
                var properties = await file.GetBasicPropertiesAsync();
                double fileSize = properties.Size / 1024;  // Get file size
                string filesizeinkb = $"{fileSize:F2} KB";

                var newItem = new FileItem { Name = file.Name, Size = filesizeinkb, Date = DateTime.Now.ToShortDateString() };
                files.Add(newItem);
            }
        }

        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            
        }

        private async void LogoutFromDB()
        {
            HttpClient GoOnlineclient = new HttpClient();
            var goonline = new
            {
                DNAddress = MainpageAddress_display.Text.Substring(1)
            };
            string goOnlineURL = "https://dbserver01.azurewebsites.net/api/OnlineNodes/GoOffline";
            string goOnlinejson = JsonConvert.SerializeObject(goonline);
            var goOnlinecontent = new StringContent(goOnlinejson, Encoding.UTF8, "application/json");
            HttpResponseMessage goOnlineresponse = await GoOnlineclient.PostAsync(goOnlineURL, goOnlinecontent);
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            LogoutFromDB();

            BlankWindow1 logW = new BlankWindow1();
            logW.Activate();

            this.Close();

        }
        private void MainWindow_Closed(object sender, WindowEventArgs e)
        {
            LogoutFromDB(); // Call your logout function
        }

    }
}
