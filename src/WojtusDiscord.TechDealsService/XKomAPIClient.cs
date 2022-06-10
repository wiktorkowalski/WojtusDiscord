using Newtonsoft.Json;
using RestSharp;

namespace WojtusDiscord.TechDealsService
{   
    public class XKomAPIClient
    {
        private const string url = "https://mobileapi.x-kom.pl/api/v1/xkom/";
        private const string resource = "hotShots/current";
        private const string apiKey = "jfsTOgOL23CN2G8Y";

        public async Task<XKomResponse> GetXKomResponseAsync()
        {
            var client = new RestClient(url);
            var request = new RestRequest(resource, Method.Get);
            request.AddHeader("x-api-key", apiKey);
            request.AddQueryParameter("onlyHeader", true);
            request.AddQueryParameter("commentAmount", 15);

            var xkomResponse = await client.GetAsync<XKomResponse>(request);

            return xkomResponse;
        }
    }
}

public class XKomResponse
{
    public string Id { get; set; }
    public object Product { get; set; }
    public double Price { get; set; }
    public double OldPrice { get; set; }
    public string PromotionGainText { get; set; }
    public List<string> PromotionGainTextLines { get; set; }
    public double PromotionGainValue { get; set; }
    public int PromotionTotalCount { get; set; }
    public int SaleCount { get; set; }
    public int MaxBuyCount { get; set; }
    public string PromotionName { get; set; }
    public DateTime PromotionEnd { get; set; }
    public PromotionPhoto PromotionPhoto { get; set; }
    public bool IsActive { get; set; }
    public bool IsSuspended { get; set; }
}

public class PromotionPhoto
{
    public string Url { get; set; }
    public string ThumbnailUrl { get; set; }
    public object UrlTemplate { get; set; }
}