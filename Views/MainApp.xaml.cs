using System;
using System.Collections.Generic;
using Windows.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Test1.Services;
using Test1.Utilities;
using Test1.Models;

namespace Test1
{
    public sealed partial class MainAppPage : Window
    {
        public static string username;
        public static string address;

        public MainAppPage(string username, string address)
        {
            this.InitializeComponent();

            // Set window size
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new SizeInt32(1200, 775));

            // Initialize UI
            MainAppPage.username = username;
            MainAppPage.address = address;
            UpdateUI(username, address);

            Blockchain.InitializeBlockchainAsync();

            // Load saved files
            //LoadFiles();

            this.Closed += MainWindow_Closed;
        }

        public async void UpdateUI(string username, string address)
        {
            string fmtadd = "@" + address;
            MainpageUsername.Text = username;
            MainpageAddress_display.Text = fmtadd;
        }

        private async void LoadFiles()
        {
            try
            {
                var files = await FileStorageService.LoadFilesAsync();
                FileListView.Items.Clear();

                foreach (var file in files)
                {
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(2, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });

                    var nameText = new TextBlock
                    {
                        Text = file.Name,
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(nameText, 0);

                    var sizeText = new TextBlock
                    {
                        Text = file.Size,
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(sizeText, 1);

                    var dateText = new TextBlock
                    {
                        Text = file.Date,
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(dateText, 2);

                    var downloadButton = new Button
                    {
                        Content = "Download",
                        Tag = file.Name,
                        Margin = new Thickness(0, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    downloadButton.Click += DownloadButton_Click;
                    Grid.SetColumn(downloadButton, 3);

                    grid.Children.Add(nameText);
                    grid.Children.Add(sizeText);
                    grid.Children.Add(dateText);
                    grid.Children.Add(downloadButton);

                    FileListView.Items.Add(grid);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading files: {ex.Message}");
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var fileName = (string)button.Tag;

            // Implement your download logic here
            //await FileHelper.DownloadFile(fileName);
        }

        private async void FabButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile file = await picker.PickSingleFileAsync();
            await FileHelper.UploadFile(file);
            /*if (file != null)
            {B
                try
                {
                    //FileStorageService.SaveFileAsync(file);
                    //await FileHelper.UploadFile(file);
                    //LoadFiles(); // Refresh the list
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error uploading file: {ex.Message}");
                }
            }*/
        }

        private void MainWindow_Closed(object sender, WindowEventArgs e)
        {
            AuthService.Logout();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            await AuthService.Logout();
            new LoginPage().Activate();
            this.Close();
        }

        private void Support_Click(object sender, RoutedEventArgs e)
        {
            FileHelper.DownloadFile();
        }
    }
}