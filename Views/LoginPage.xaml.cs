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
using Test1.Services;

namespace Test1
{

    public sealed partial class LoginPage : Window
    {

        public LoginPage()
        {
            this.InitializeComponent();
        }

        private async void savebtn_Click(object sender, RoutedEventArgs e)
        {

            string authResult = await AuthService.LoginUser(UsernameTextBox.Text, PassWord.Password);

            if (authResult == "None")
                ErrorMessageTextBlock.Text = "Invalid username or password.";
            else
            {
                MainAppPage mainAppWindow = new MainAppPage(UsernameTextBox.Text, authResult);
                mainAppWindow.Activate();
                this.Close();
            }
        }

        private void cancelbtn_Click(object sender, RoutedEventArgs e)
        {
            UsernameTextBox.Text = "";
            PassWord.Password = "";

        }

        private void RegPage_Click(object sender, RoutedEventArgs e)
        {
            RegistrationPage RegPage = new RegistrationPage();
            RegPage.Activate();
            this.Close();
        }
    }
}
