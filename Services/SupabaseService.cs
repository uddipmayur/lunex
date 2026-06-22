using System;
using Supabase;

namespace Lunex.Services
{
    public static class SupabaseService
    {
        private const string SupabaseUrl = "https://qhihazzhrtiiwphtinpj.supabase.co";
        private const string SupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InFoaWhhenpocnRpaXdwaHRpbnBqIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODIwOTMxOTEsImV4cCI6MjA5NzY2OTE5MX0.xgNLwwsZG7U6hevffRXfnHkYsufDrcrS9VUuANRtoYw";

        private static Client? _client;

        public static Client Client
        {
            get
            {
                if (_client == null)
                {
                    var options = new SupabaseOptions { AutoConnectRealtime = false };
                    _client = new Client(SupabaseUrl, SupabaseAnonKey, options);
                    _client.InitializeAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                }

                return _client;
            }
        }
    }
}
