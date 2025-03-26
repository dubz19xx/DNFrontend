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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Test1
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class RegistrationPage : Window
    {
        public RegistrationPage()
        {
            this.InitializeComponent();
        }


        static string GenerateMasterKey(int byteLength)
        {
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256; // AES-256
                aes.GenerateKey();
                aes.GenerateIV();

                // Convert key and IV to Base64 strings
                string keyBase64 = Convert.ToBase64String(aes.Key);
                string ivBase64 = Convert.ToBase64String(aes.IV);

                // Save to file as: key;iv
                string content = $"{keyBase64};{ivBase64}";
                return content;
            }
        }
        public static bool IsValidEmail(string email)
        {
            string pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
            return Regex.IsMatch(email, pattern);
        }

        private async void savebtn_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameTextBox.Text;
            string email = EmailTextBox.Text.Trim();
            string password = PassWord.Password;
            
            // email format validator
            if (!IsValidEmail(email))
            {
                ErrorMessageTextBlock.Text = "Invalid email format!";
                return;
            }

            HttpClient client = new HttpClient();
            string Registerurl = "https://dbserver01.azurewebsites.net/api/LoginInfoes/register";

            var newUser = new
            {
                Username = username,
                DNAddress = "none",
                EmailId = email,
                Password = password
            };

            string jsondata = JsonConvert.SerializeObject(newUser);
            var content = new StringContent(jsondata, Encoding.UTF8, "application/json");
            HttpResponseMessage postresponse = await client.PostAsync(Registerurl, content);
            string responseContent = await postresponse.Content.ReadAsStringAsync();

            if (postresponse.IsSuccessStatusCode)
            {
                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string dnStorePath = Path.Combine(documentsPath, "DNStore");
                string userFolderPath = Path.Combine(dnStorePath, username);

                if (!Directory.Exists(dnStorePath))
                {
                    Directory.CreateDirectory(dnStorePath);
                }

                if (!Directory.Exists(userFolderPath))
                {
                    Directory.CreateDirectory(userFolderPath);
                }

                string mainstoragePath = Path.Combine(userFolderPath, "storage");
                string uploadqueuePath = Path.Combine(userFolderPath, "uploadData");
                string configPath = Path.Combine(userFolderPath, "config");

                if (!Directory.Exists(mainstoragePath))
                {
                    Directory.CreateDirectory(mainstoragePath);
                }

                if (!Directory.Exists(uploadqueuePath))
                {
                    Directory.CreateDirectory(uploadqueuePath);
                }


                if (!Directory.Exists(configPath))
                {
                    Directory.CreateDirectory(configPath);
                }



                string masterKey = GenerateMasterKey(32);
                string secretfile = Path.Combine(configPath, "secret.txt");
                File.WriteAllText(secretfile, masterKey);
                BlankWindow1 logW = new BlankWindow1();
                logW.Activate();
                this.Close();
            }
            else
            {
                // If not a success, handle error response
                ErrorMessageTextBlock.Text = responseContent; // Display the error message in a UI element
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
