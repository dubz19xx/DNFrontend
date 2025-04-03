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
using Newtonsoft.Json;
using System.Net.Http;
using System.Text;
using System.Net;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Test1.Utilities;
using Test1.Services;

namespace Test1
{
    public sealed partial class RegistrationPage : Window
    {
        public RegistrationPage()
        {
            this.InitializeComponent();
        }

        private async void savebtn_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameTextBox.Text;
            string email = EmailTextBox.Text.Trim();
            string password = PassWord.Password;
            
            NetworkService networkService = new NetworkService();
            FileHelper fileHelper = new FileHelper(username);

            //start validation here

            bool validEmail = Validation.IsValidEmail(email);
            if (!validEmail)
            {
                ErrorMessageTextBlock.Text = "Invalid email format!";
                return;
            }

            //[!] TODO: add password validation  (8digits etc)


            HttpResponseMessage registerReponse = await AuthService.RegisterUser(username, password, email);
            string responseContent = await registerReponse.Content.ReadAsStringAsync();

            if(!registerReponse.IsSuccessStatusCode) 
                ErrorMessageTextBlock.Text = responseContent;
            else
            {
                fileHelper.SetupFolders();
                string masterKey = CryptHelper.GenerateMasterKey(32);
                fileHelper.SaveMasterKey(masterKey);

                LoginPage loginWindow = new LoginPage();
                loginWindow.Activate();
                this.Close();

            }
        }


        private void cancelbtn_Click(object sender, RoutedEventArgs e)
        {
            UsernameTextBox.Text = "";
            PassWord.Password = "";
            EmailTextBox.Text = string.Empty;

        }
    }
}
