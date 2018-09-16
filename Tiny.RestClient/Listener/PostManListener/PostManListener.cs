﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Tiny.RestClient.PostMan;

namespace Tiny.RestClient
{
    /// <summary>
    /// A listener which will create a postMan collection/>
    /// </summary>
    public class PostManListener : IListener
    {
        private readonly object _toLock = new object();

        /// <summary>
        ///  Initializes a new instance of the <see cref="PostManListener"/> class.
        /// </summary>
        /// <param name="name">name of the postMan collection</param>
        public PostManListener(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name of collection can't be null or empty", nameof(name));
            }

            Collection = new PostManCollection
            {
                Info = new Info
                {
                    PostmanId = Guid.NewGuid().ToString(),
                    Name = name,
                    Schema = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
                },
                Items = new List<Item>()
            };
        }

        internal PostMan.PostManCollection Collection { get; }

        /// <inheritdoc/>
        public bool MeasureTime => false;

        /// <summary>
        /// Save PostManCollection to file
        /// </summary>
        /// <param name="fileInfo">file where the collection is saved</param>
        /// <returns>return a <see cref="Task"/></returns>
        public async Task SaveAsync(FileInfo fileInfo)
        {
            if (fileInfo.Exists)
            {
                fileInfo.Delete();
            }

            byte[] encodedText = Encoding.UTF8.GetBytes(GetCollectionJson());
            using (FileStream fileStream = fileInfo.OpenWrite())
            {
                await fileStream.WriteAsync(encodedText, 0, encodedText.Length);
            }
        }

        /// <summary>
        /// Get postman collection json data
        /// </summary>
        /// <returns>return a post man collection json file</returns>
        public string GetCollectionJson()
        {
            lock (_toLock)
            {
                var serializer = new JsonSerializer
                {
                    NullValueHandling = NullValueHandling.Ignore
                };
                using (var stringWriter = new StringWriter(new StringBuilder(1024), CultureInfo.InvariantCulture))
                {
                    using (var jsonTextWriter = new JsonTextWriter(stringWriter))
                    {
                        jsonTextWriter.Formatting = Formatting.Indented;
                        serializer.Serialize(jsonTextWriter, Collection, typeof(PostManCollection));
                    }

                    return stringWriter.ToString();
                }
            }
        }

        /// <inheritdoc/>
        public Task OnFailedToReceiveResponseAsync(Uri uri, HttpMethod httpMethod, Exception exception, TimeSpan? elapsedTime)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task OnReceivedResponseAsync(Uri uri, HttpMethod httpMethod, HttpResponseMessage response, TimeSpan? elapsedTime)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task OnSendingRequestAsync(Uri uri, HttpMethod httpMethod, HttpRequestMessage httpRequestMessage)
        {
            var segments = string.Join("_", SegmentsWithoutSlash(uri));
            var item = new Item
            {
                Name = $"{httpMethod.Method}_{segments}",
                Request = await GetRequestAsync(uri, httpMethod, httpRequestMessage)
            };

            lock (_toLock)
            {
                Collection.Items.Add(item);
            }
        }

        private async Task<PostMan.Request> GetRequestAsync(Uri uri, HttpMethod httpMethod, HttpRequestMessage httpRequestMessage)
        {
            var request = new PostMan.Request
            {
                Method = httpMethod.Method,
                Headers = GetHeaders(httpRequestMessage),
                Url = GetUrl(uri),
                Body = await GetBodyAsync(httpRequestMessage),
            };
            return request;
        }

        private List<Header> GetHeaders(HttpRequestMessage httpRequestMessage)
        {
            var headers = new List<Header>();
            foreach (var header in httpRequestMessage.Headers)
            {
                foreach (var currentValue in header.Value)
                {
                    headers.Add(new Header() { Key = header.Key, Value = currentValue });
                }
            }

            if (httpRequestMessage.Content != null)
            {
                foreach (var header in httpRequestMessage.Content.Headers)
                {
                    foreach (var currentValue in header.Value)
                    {
                        headers.Add(new Header() { Key = header.Key, Value = currentValue });
                    }
                }
            }

            return headers;
        }

        private async Task<Body> GetBodyAsync(HttpRequestMessage httpRequestMessage)
        {
            if (httpRequestMessage.Content != null)
            {
                Body body = new Body
                {
                    Mode = "raw",
                    Raw = await httpRequestMessage.Content.ReadAsStringAsync()
                };
                return body;
            }

            return null;
        }

        private Url GetUrl(Uri uri)
        {
            var url = new PostMan.Url
            {
                Raw = uri.OriginalString,
                Protocol = uri.Scheme,
                Host = uri.Host.Split('.'),
                Path = SegmentsWithoutSlash(uri),
                Port = uri.Port.ToString(),
                QueryParameters = GetQuery(uri)
            };

            return url;
        }

        private string[] SegmentsWithoutSlash(Uri uri)
        {
            return uri.Segments.
                Select(s => s.Replace("/", string.Empty)).
                Where(s => s != string.Empty).
                ToArray();
        }

        private List<Query> GetQuery(Uri uri)
        {
            if (string.IsNullOrEmpty(uri.Query))
            {
                return null;
            }

            var queries = new List<Query>();

            // TODO : rewrite this code
            var arguments = uri.Query
            .Substring(1) // Remove '?'
            .Split('&')
            .Select(q => q.Split('='))
            .ToDictionary(q => q.FirstOrDefault(), q => q.Skip(1).FirstOrDefault());

            foreach (var item in arguments)
            {
                queries.Add(new Query() { Key = item.Key, Value = item.Value });
            }

            return queries;
        }
    }
}
