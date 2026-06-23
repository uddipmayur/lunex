using System;
using Supabase;

namespace Lunex.Services
{
    public static class SupabaseService
    {
        private const string SupabaseUrl = "ENTER YOUR KEY";
        private const string SupabaseAnonKey = "ENTER YOUR KEY";

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
