using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace MolioDocEF6
{
    public class SpecToolClient
    {
        static HttpClient http = new HttpClient();

        public static async Task<SpecToolClient> Login(Uri baseUrl, string username, string password)
        {
            using (var request = new HttpRequestMessage())
            {
                var content = new StringContent(JsonConvert.SerializeObject(new { username, password }), Encoding.UTF8, "application/json");
                var response = await http.PostAsync(UrlTo(baseUrl, "api/spectool/user/login"), content);

                var authCookie = GetAspxAuthCookie(response);
                if (authCookie == null)
                    throw new Exception("Invalid credentials");

                return new SpecToolClient(baseUrl, authCookie);
            }
        }

        static HttpCookie GetAspxAuthCookie(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("set-cookie", out var cookies))
                return cookies
                    .Select(c => HttpCookie.TryParse(c, out var cookie) ? cookie : null)
                    .Where(c => c != null & c.Name == ".ASPXAUTH")
                    .FirstOrDefault();

            return null;
        }

        public async Task<IEnumerable<SpecToolWorkArea>> GetWorkAreas()
        {
            using (var request = AuthenticatedRequestMessage("api/spectool/workarea/getworkareas"))
            {
                var response = await http.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<IEnumerable<SpecToolWorkArea>>(content);
            }
        }

        public async Task<IEnumerable<SpecToolDocument>> GetDocuments(string id)
        {
            using (var request = AuthenticatedRequestMessage("api/spectool/workarea/getdocuments", "id=" + id))
            {
                var response = await http.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<IEnumerable<SpecToolDocument>>(content);
            }
        }

        public async Task<SpecToolDocument> GetDocument(Guid id)
        {
            using (var request = AuthenticatedRequestMessage("api/spectool/workarea/getdocument", "id=" + id))
            {
                var response = await http.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<SpecToolDocument>(content);
            }
        }

        SpecToolClient(Uri baseUrl, HttpCookie authCookie)
        {
            this.baseUrl = baseUrl;
            this.authCookie = authCookie;
        }

        HttpRequestMessage AuthenticatedRequestMessage(string path, string queryString = "")
        {
            var request = new HttpRequestMessage();
            request.RequestUri = UrlTo(baseUrl, path, queryString);
            request.Headers.Add("Cookie", $"{authCookie.Name}={authCookie.Value}");
            return request;
        }

        static Uri UrlTo(Uri baseUrl, string path, string queryString = "") =>
            new UriBuilder(baseUrl.Scheme, baseUrl.Host, baseUrl.Port, path, "?" + queryString).Uri;

        Uri baseUrl;
        HttpCookie authCookie;
    }

    public class SpecToolWorkArea
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Access { get; set; }
    }

    public class SpecToolDocument
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public string WorkAreaId { get; set; }

        public string WorkAreaName { get; set; }

        public string Type { get; set; }

        public IEnumerable<SpecToolSection> Sections { get; set; }

        public string DocumentType { get; set; }

        public SpecToolDocument Document { get; set; }
    }

    public class SpecToolSection
    {
        public Guid Id { get; set; }

        public string Title { get; set; }

        public string Number { get; set; }

        public IEnumerable<SpecToolContentGroup> Groups { get; set; }

        public IEnumerable<SpecToolSection> Sections { get; set; }
    }

    public class SpecToolContentGroup
    {
        public string Name { get; set; }

        public IEnumerable<SpecToolContentItem> Contents { get; set; }
    }

    public class SpecToolContentItem
    {
        public string Id { get; set; }

        public bool IsMasterText { get; set; }

        public string MasterTextId { get; set; }

        public string Text { get; set; }

        public string MasterText { get; set; }
    }
}
