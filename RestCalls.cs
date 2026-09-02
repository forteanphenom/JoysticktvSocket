using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Encodings.Web;
using System.Web;

namespace JoysticktvSocket;

public class JoystickRestAPI
{
    private HttpClient client = new HttpClient();

    private string _basicKey;

    private string _redirectURI = string.Empty;

    public JoystickRestAPI(string clientID, string clientSecret)
    {
        byte[] plainTextBytes = Encoding.UTF8.GetBytes($"{clientID}:{clientSecret}");
        _basicKey = Convert.ToBase64String(plainTextBytes);
    }

    public JoystickRestAPI(string clientID, string clientSecret, string redirectURI)
    {
        byte[] plainTextBytes = Encoding.UTF8.GetBytes($"{clientID}:{clientSecret}");
        _basicKey = Convert.ToBase64String(plainTextBytes);

        _redirectURI = HttpUtility.UrlEncode(redirectURI);
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
        string queryParams = $"redirect_uri={((_redirectURI == string.Empty) ? "unused" : _redirectURI)}" +
            $"&code={code}" +
            $"&grant_type=authorization_code";

        string uri = "https://api.joystick.tv/api/oauth/token?" + queryParams;

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

        string uri = "https://api.joystick.tv/api/oauth/token?" + queryParams;

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

        AccessTokenMessage result;

        try
        {
            result = JsonSerializer.Deserialize<AccessTokenMessage>(new_response_string);
        }
        catch (Exception ex)
        {
            result = new AccessTokenMessage()
            {
                refresh_token = "ERROR",
                access_token = "ERROR",
                raw_data = new_response_string
            };
        }

        if (result.access_token == string.Empty)
        {
            result.raw_data = new_response_string;
        }

        return result;
    }


    public StreamSettingsMessage GetStreamSettings(string accessToken)
    {
        string uri = "https://api.joystick.tv/api/v1/me/identity";

        using var request = new HttpRequestMessage()
        {
            RequestUri = new Uri(uri),
            Method = HttpMethod.Get,
            Content = new StringContent("", Encoding.UTF8, "application/json")
        };

        if (request.Headers.Contains("Authorization"))
            request.Headers.Remove("Authorization");
        if (request.Headers.Contains("Accept"))
            request.Headers.Remove("Accept");

        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Headers.Add("Accept", "application/json");

        var new_response = client.SendAsync(request).Result;

        string new_response_string = new_response.Content.ReadAsStringAsync().Result;

        StreamSettingsMessage result;

        try
        {
            result = JsonSerializer.Deserialize<StreamSettingsMessage>(new_response_string);
        }
        catch (Exception ex) {

            Console.WriteLine("Could Not Parse JSON in GetStreamSettings: " + new_response_string);

            return new StreamSettingsMessage() { };
        }

        if (result.channel_id == string.Empty)
            Console.WriteLine("Faulty Stream Settings Message: " + new_response_string);

        return result;
    }

    public List<string> GetSubscribers(string accessToken)
    {
        string uri = "https://api.joystick.tv/api/v1/subscribers?per_page=25&page=";

        List<string> result = new List<string>();

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
            if (request.Headers.Contains("Accept"))
                request.Headers.Remove("Accept");

            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            request.Headers.Add("Accept", "application/json");

            var new_response = client.SendAsync(request).Result;

            string new_response_string;
            new_response_string = new_response.Content.ReadAsStringAsync().Result;

            SubscriberMessage subscriberMessage;

            try
            {
                subscriberMessage = JsonSerializer.Deserialize<SubscriberMessage>(new_response_string);

            }

            catch (Exception ex)
            {
                Console.WriteLine(new_response_string);
                return new();
            }

            maxpages = subscriberMessage.pagination.total_pages;

            for (int i = 0; i < subscriberMessage.items.Count; i++)
            {
                result.Add(subscriberMessage.items[i].username);
            }

            page++;
        }

        return result;
    }

}
