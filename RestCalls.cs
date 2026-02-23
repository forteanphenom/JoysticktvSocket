using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace JoysticktvSocket;

public class JoystickRestAPI
{
    private HttpClient client = new HttpClient();

    private string _basicKey;

    public JoystickRestAPI(string clientID, string clientSecret)
    {
        byte[] plainTextBytes = Encoding.UTF8.GetBytes($"{clientID}:{clientSecret}");
        _basicKey = Convert.ToBase64String(plainTextBytes); ;
    }

    public Dictionary<string, AccessTokenMessage> RefreshAccessTokens(List<string> refreshTokens, out Dictionary<string, string> rejectedTokens)
    {
        Dictionary<string, AccessTokenMessage> newTokens = new Dictionary<string, AccessTokenMessage>();

        rejectedTokens = new();

        foreach (string token in refreshTokens)
        {
            AccessTokenMessage newToken = NewAccessTokenFromRefresh(token);

            if (!newTokens.ContainsKey(token) && newToken.expires_in > 0)
                newTokens.Add(token, newToken);
            else
                rejectedTokens.Add(token, newToken.raw_data);
        }

        return newTokens;
    }


    public AccessTokenMessage GetToken(string code)
    {
        string queryParams = $"redirect_uri=unused" +
            $"&code={code}" +
            $"&grant_type=authorization_code";

        string uri = "https://joystick.tv/api/oauth/token?" + queryParams;

        using var request = new HttpRequestMessage()
        {
            RequestUri = new Uri(uri),
            Method = HttpMethod.Post,
            Content = new StringContent(queryParams, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        if (request.Headers.Contains("Authorization")) request.Headers.Remove("Authorization");
        if (request.Headers.Contains("Accept")) request.Headers.Remove("Accept");

        request.Headers.Add("Authorization", $"Basic {_basicKey}");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-JOYSTICK-STATE", "unused");

        var new_response = client.SendAsync(request).Result;
        string new_response_string = new_response.Content.ReadAsStringAsync().Result;

        AccessTokenMessage result = JsonSerializer.Deserialize<AccessTokenMessage>(new_response_string);

        if (result.access_token == string.Empty)
            Console.WriteLine("Faulty Access Token Message: " + new_response_string);

        return result;
    }

    public AccessTokenMessage NewAccessTokenFromRefresh(string refreshToken)
    {
        string queryParams = $"refresh_token={refreshToken}" +
            $"&grant_type=refresh_token";

        string uri = "https://joystick.tv/api/oauth/token?" + queryParams;

        using var request = new HttpRequestMessage()
        {
            RequestUri = new Uri(uri),
            Method = HttpMethod.Post,
            Content = new StringContent(queryParams, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        if (request.Headers.Contains("Authorization")) request.Headers.Remove("Authorization");
        if (request.Headers.Contains("Accept")) request.Headers.Remove("Accept");

        request.Headers.Add("Authorization", $"Basic {_basicKey}");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-JOYSTICK-STATE", "unused");

        var new_response = client.SendAsync(request).Result;

        string new_response_string = new_response.Content.ReadAsStringAsync().Result;

        AccessTokenMessage result = JsonSerializer.Deserialize<AccessTokenMessage>(new_response_string);

        if (result.access_token == string.Empty)
        {
            result.raw_data = new_response_string;
        }

        return result;
    }


    public StreamSettingsMessage GetStreamSettings(string accessToken)
    {
        string uri = "https://joystick.tv/api/users/stream-settings";

        using var request = new HttpRequestMessage()
        {
            RequestUri = new Uri(uri),
            Method = HttpMethod.Get,
            Content = new StringContent("", Encoding.UTF8, "application/json")
        };

        if (request.Headers.Contains("Authorization"))
            request.Headers.Remove("Authorization");

        request.Headers.Add("Authorization", $"Bearer {accessToken}");


        var new_response = client.SendAsync(request).Result;

        string new_response_string = new_response.Content.ReadAsStringAsync().Result;

        StreamSettingsMessage result = JsonSerializer.Deserialize<StreamSettingsMessage>(new_response_string);

        if (result.channel_id == string.Empty)
            Console.WriteLine("Faulty Stream Settings Message: " + new_response_string);

        return result;
    }

    public List<SubscriberItems> GetSubscribers(string accessToken, bool activeOnly = true)
    {
        string uri = "https://joystick.tv/api/users/subscriptions?per_page=25&page=";

        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        List<SubscriberItems> result = new List<SubscriberItems>();

        int page = 1;
        int maxpages = 1;

        while (page <= maxpages)
        {

            using var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(uri + page),
                Method = HttpMethod.Get,
                Content = new StringContent("", Encoding.UTF8, "application/json")
            };

            if (request.Headers.Contains("Authorization"))
                request.Headers.Remove("Authorization");

            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var new_response = client.SendAsync(request).Result;

            string new_response_string = new_response.Content.ReadAsStringAsync().Result;

            SubscriberMessage subscriberMessage = JsonSerializer.Deserialize<SubscriberMessage>(new_response_string);

            maxpages = subscriberMessage.pagination.total_pages;

            if (activeOnly)
            {
                for (int i = 0; i < subscriberMessage.items.Count; i++)
                {
                    if (String.Compare(subscriberMessage.items[i].expires_at, today) >= 0)
                        result.Add(subscriberMessage.items[i]);
                }
            }
            else
            {
                for (int i = 0; i < subscriberMessage.items.Count; i++)
                {
                    result.Add(subscriberMessage.items[i]);
                }

            }

            page++;
        }

        return result;
    }
}
