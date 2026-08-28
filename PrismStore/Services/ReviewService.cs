using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Headers;

namespace PrismStore.Services
{
    public static class ReviewService
    {
        private static readonly HttpClient client = new HttpClient();
        private const string BaseApiUrl = "https://gdcr.dankassassin368.com/ns-status/reviews";

        public static async Task<string> GetReviews(string appId)
        {
            try
            {
                string url = $"{BaseApiUrl}/{appId}?t={DateTime.UtcNow.Ticks}";
                var response = await client.GetAsync(new Uri(url));
                string result = await response.Content.ReadAsStringAsync();

                Debug.WriteLine("[GET REVIEWS] " + result);
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[GET REVIEWS ERROR] " + ex.Message);
                return "[]";
            }
        }

        public static async Task<string> SubmitReview(string token, string jsonBody)
        {
            try
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    new Uri(BaseApiUrl)
                );

                request.Content = new HttpStringContent(
                    jsonBody,
                    UnicodeEncoding.Utf8,
                    "application/json"
                );

                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization = new HttpCredentialsHeaderValue("Bearer", token);
                }

                var response = await client.SendRequestAsync(request);
                string result = await response.Content.ReadAsStringAsync();

                Debug.WriteLine("[POST REVIEW] " + result);
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[POST REVIEW ERROR] " + ex.Message);
                throw;
            }
        }
    }
}
