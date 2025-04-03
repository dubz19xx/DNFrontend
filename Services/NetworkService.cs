﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Test1.Services
{
    public class NetworkService
    {
        private static string baseURL = "https://dbserver01.azurewebsites.net/api/";
        public static async Task<HttpResponseMessage> SendGetRequest(string URL)
        {
            string requestURL = baseURL + URL;
            HttpClient client = new HttpClient();
            HttpResponseMessage response = await client.GetAsync(requestURL);
            return response;
        }

        public static async Task<HttpResponseMessage> SendPostRequest(string URL, HttpContent data)
        {
            string requestURL = baseURL + URL;
            HttpClient client = new HttpClient();
            HttpResponseMessage response = await client.PostAsync(requestURL, data);
            return response;
        }

    }
}
