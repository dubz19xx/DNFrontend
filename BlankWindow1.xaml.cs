using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Threading.Tasks;
using Microsoft.UI;
using WinRT.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Composition;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;
using System;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Newtonsoft.Json;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Test1
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class BlankWindow1 : Window
    {

        private async Task<bool> AuthenticateUser(string username, string password)
        {
            HttpClient client = new HttpClient();
            string Loginurl = "https://dbserver01.azurewebsites.net/api/LoginInfoes/login";
            var loginuser = new
            {
                username = username,
                password = password
            };
            string jsondata = JsonConvert.SerializeObject(loginuser);
            var content = new StringContent(jsondata, Encoding.UTF8, "application/json");
            HttpResponseMessage postresponse = await client.PostAsync(Loginurl, content);

            if (postresponse.IsSuccessStatusCode)
            {
                string selfAddress = await postresponse.Content.ReadAsStringAsync();
                HttpClient GoOnlineclient = new HttpClient();
                var goonline = new
                {
                    DNAddress = selfAddress
                };

                

                string goOnlineURL = "https://dbserver01.azurewebsites.net/api/OnlineNodes/GoOnline";
                string goOnlinejson = JsonConvert.SerializeObject(goonline);
                var goOnlinecontent = new StringContent(goOnlinejson, Encoding.UTF8, "application/json");
                HttpResponseMessage goOnlineresponse = await GoOnlineclient.PostAsync(goOnlineURL, goOnlinecontent);
                if (goOnlineresponse.IsSuccessStatusCode)
                {
                    MainWindow m_window = new MainWindow();

                    m_window.UpdateUI(username, selfAddress);
                    m_window.InitalizeBlockchain(selfAddress);
                    m_window.Activate();

                    this.Close();

                }

                return false;
            }
            return false;
        }

        public BlankWindow1()
        {
            this.InitializeComponent();
        }

        private async void savebtn_Click(object sender, RoutedEventArgs e)
        {
           bool isValid = await AuthenticateUser(UsernameTextBox.Text, PassWord.Password);
            if (!isValid) {
                // Display error message if login fails
                ErrorMessageTextBlock.Text = "Invalid username or password.";
            }
        }

        private void cancelbtn_Click(object sender, RoutedEventArgs e)
        {
            UsernameTextBox.Text = "";
            PassWord.Password = "";

        /*    MainWindow m_window = new MainWindow();
            m_window.Activate();

            this.Close(); */
        }

        private void RegPage_Click(object sender, RoutedEventArgs e)
        {
            RegistrationPage RegW = new RegistrationPage();
            RegW.Activate();

            this.Close();
        }
    }
}
