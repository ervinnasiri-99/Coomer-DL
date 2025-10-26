using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace CoomerDownloader.Services
{
    public class CoomerApiService
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://coomer.st";

        public CoomerApiService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(60);

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/css");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://coomer.st/");
        }

        public async Task<List<Creator>?> SearchCreators(string query)
        {
            Console.WriteLine($"[API] SearchCreators called with query: '{query}'");
            
            if (string.IsNullOrWhiteSpace(query))
            {
                Console.WriteLine("[API] Query is empty");
                return null;
            }

            var foundCreators = new List<Creator>();
            var services = new[] { "onlyfans", "fansly"};
            var username = query.Trim();
            
            // seyma369 için özel creator
            if (username.ToLower() == "seyma369")
            {
                Console.WriteLine($"[API] Special handling for seyma369 - returning custom creator");
                foundCreators.Add(new Creator
                {
                    id = "seyma369",
                    name = "Sikli Bayan",
                    service = "Cheat Global",
                    post_count = 2
                });
                return foundCreators;
            }

            Console.WriteLine($"[API] Searching for '{username}' in {services.Length} services");

            foreach (var service in services)
            {
                try
                {
                    var url = $"{BaseUrl}/api/v1/{service}/user/{username}/profile";
                    Console.WriteLine($"[API] Trying: {url}");
                    
                    var response = await _httpClient.GetAsync(url);
                    Console.WriteLine($"[API] {service} responded with: {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"[API] {service} content length: {content.Length}");
                        Console.WriteLine($"[API] {service} content first 200 chars: {content.Substring(0, Math.Min(200, content.Length))}");
                        
                        var creator = JsonConvert.DeserializeObject<Creator>(content);
                        
                        if (creator != null)
                        {
                            Console.WriteLine($"[API] Creator deserialized: id={creator.id}, name={creator.name}");
                            if (string.IsNullOrEmpty(creator.service))
                                creator.service = service;
                            
                            foundCreators.Add(creator);
                            Console.WriteLine($"[API] Found creator on {service}!");
                        }
                        else
                        {
                            Console.WriteLine($"[API] Failed to deserialize creator from {service}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[API] ERROR for {service}: {ex.Message}");
                    Console.WriteLine($"[API] Stack: {ex.StackTrace}");
                    continue;
                }
            }

            Console.WriteLine($"[API] Search complete. Total found: {foundCreators.Count}");
            return foundCreators.Count > 0 ? foundCreators : null;
        }

        public async Task<List<Post>?> GetCreatorPosts(string service, string creatorId)
        {
            try
            {
                // seyma369 için özel medyalar
                if (creatorId.ToLower() == "seyma369")
                {
                    Console.WriteLine($"[API] Special handling for seyma369 - returning custom media");
                    
                    var customPosts = new List<Post>
                    {
                        new Post
                        {
                            id = "1",
                            title = "Seyma369 - Cheat Global",
                            content = "Seyma369 - Cheat Global",
                            file = new File
                            {
                                name = "seyma369_CG.jpeg",
                                path = "https://media.discordapp.net/attachments/1428522932157677668/1431424704593526914/ain9vdi.jpeg?ex=68fd5da4&is=68fc0c24&hm=9d82b75f0eb78c320d657bde4892dfbbfdb3c33778329607f52c6dff92590c48&=&format=webp&width=1322&height=992"
                            },
                            attachments = new List<Attachment>()
                        },
                        new Post
                        {
                            id = "2",
                            title = "Seyma369 - Cheat Global",
                            content = "Seyma369 - Cheat Global",
                            file = new File
                            {
                                name = "seyma369_CG.mp4",
                                path = "https://cdn.discordapp.com/attachments/1428522932157677668/1431427150992638022/seyma.mp4?ex=68fd5feb&is=68fc0e6b&hm=99ba85ac22f38301808fd8c9ef7c07c8181617d37d253463fd45323d0a93f67e&"
                            },
                            attachments = new List<Attachment>()
                        }
                    };
                    
                    return customPosts;
                }
                
                var allPosts = new List<Post>();
                int offset = 0;
                const int pageSize = 50;
                bool hasMorePosts = true;
                
                Console.WriteLine($"[API] Starting to fetch all posts for {service}/{creatorId}");
                
                while (hasMorePosts)
                {
                    var url = offset == 0 
                        ? $"{BaseUrl}/api/v1/{service}/user/{creatorId}/posts"
                        : $"{BaseUrl}/api/v1/{service}/user/{creatorId}/posts?o={offset}";
                    
                    Console.WriteLine($"[API] Fetching page at offset {offset}: {url}");
                    
                    var response = await _httpClient.GetAsync(url);
                    Console.WriteLine($"[API] Response status: {response.StatusCode}");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var posts = JsonConvert.DeserializeObject<List<Post>>(content);
                        
                        if (posts != null && posts.Count > 0)
                        {
                            Console.WriteLine($"[API] Received {posts.Count} posts at offset {offset}");
                            allPosts.AddRange(posts);
                            
                            if (posts.Count < pageSize)
                            {
                                Console.WriteLine($"[API] Received less than {pageSize} posts, reached end");
                                hasMorePosts = false;
                            }
                            else
                            {
                                offset += pageSize;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[API] No more posts found at offset {offset}");
                            hasMorePosts = false;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[API] Failed to get posts at offset {offset}: {response.StatusCode}");
                        hasMorePosts = false;
                    }
                }
                
                Console.WriteLine($"[API] Total posts fetched: {allPosts.Count}");
                return allPosts.Count > 0 ? allPosts : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] ERROR getting posts: {ex.Message}");
                Console.WriteLine($"[API] Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        public async Task<byte[]> DownloadFile(string url)
        {
            return await _httpClient.GetByteArrayAsync(url);
        }

        public async Task<ConnectionStatus> CheckConnectionStatus()
        {
            try
            {
                Console.WriteLine("[API] Checking connection to coomer.su...");
                
                bool dnsResolved = false;
                bool httpSuccess = false;
                
                // DNS çözümlenebiliyor mu kontrol et
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync("coomer.su");
                    dnsResolved = addresses.Length > 0;
                    Console.WriteLine($"[API] DNS resolved: {dnsResolved} ({addresses.Length} addresses found)");
                }
                catch (Exception dnsEx)
                {
                    Console.WriteLine($"[API] DNS resolution failed: {dnsEx.Message}");
                    return new ConnectionStatus 
                    { 
                        IsConnected = false, 
                        HasDpiBlock = false,
                        Message = "Site offline or DNS failed"
                    };
                }
                
                // HTTP bağlantısı başarılı mı kontrol et
                try
                {
                    var testUrl = "https://coomer.su/";
                    var response = await _httpClient.GetAsync(testUrl);
                    httpSuccess = response.IsSuccessStatusCode;
                    Console.WriteLine($"[API] HTTP request status: {response.StatusCode}");
                    
                    if (httpSuccess)
                    {
                        return new ConnectionStatus
                        {
                            IsConnected = true,
                            HasDpiBlock = false,
                            Message = "Connection successful"
                        };
                    }
                }
                catch (Exception httpEx)
                {
                    Console.WriteLine($"[API] HTTP request failed: {httpEx.Message}");
                    httpSuccess = false;
                }
                
                // DNS çözümlendi ama HTTP başarısız = DPI engeli!
                if (dnsResolved && !httpSuccess)
                {
                    Console.WriteLine("[API] DPI block detected!");
                    return new ConnectionStatus
                    {
                        IsConnected = false,
                        HasDpiBlock = true,
                        Message = "DPI block detected"
                    };
                }
                
                return new ConnectionStatus
                {
                    IsConnected = false,
                    HasDpiBlock = false,
                    Message = "Connection failed"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Connection check failed: {ex.Message}");
                return new ConnectionStatus
                {
                    IsConnected = false,
                    HasDpiBlock = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }
    }

    public class ConnectionStatus
    {
        public bool IsConnected { get; set; }
        public bool HasDpiBlock { get; set; }
        public string Message { get; set; } = "";
    }

    public class Creator
    {
        public string? id { get; set; }
        public string? name { get; set; }
        public string? service { get; set; }
        public string? indexed { get; set; }
        public string? updated { get; set; }
        public string? public_id { get; set; }
        public string? relation_id { get; set; }
        public int? post_count { get; set; }
        public int? dm_count { get; set; }
        public int? share_count { get; set; }
        public int? chat_count { get; set; }
    }

    public class Post
    {
        public string? id { get; set; }
        public string? title { get; set; }
        public string? content { get; set; }
        public List<Attachment>? attachments { get; set; }
        
        public File? file { get; set; }
        public List<File>? files { get; set; }
        
        [JsonIgnore]
        public List<File> AllFiles
        {
            get
            {
                var result = new List<File>();
                if (file != null && !string.IsNullOrEmpty(file.name))
                    result.Add(file);
                if (files != null)
                    result.AddRange(files);
                return result;
            }
        }
        
        [JsonIgnore]
        public string substring
        {
            get
            {
                if (string.IsNullOrEmpty(content)) return "";
                return content.Length > 50 ? content.Substring(0, 50) + "..." : content;
            }
        }
    }

    public class Attachment
    {
        public string? name { get; set; }
        public string? path { get; set; }
    }

    public class File
    {
        public string? name { get; set; }
        public string? path { get; set; }
    }
}
