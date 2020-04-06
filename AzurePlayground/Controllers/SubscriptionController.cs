using Microsoft.Graph;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace AzureADSubscription.Controllers
{
    public class SubscriptionController : ApiController
    {
        private const string ConnectionString = "{{Only used for logging purposes}}";
        private const string TenantId = "{{Directory (tenant) ID}}";
        private const string ApplicationId = "{{Application (client) ID}}";
        private const string ApplicationSecret = "{{Application Secret Key}}";

        // GET api/subscription/
        public async Task<string> Get() {
            GraphServiceClient graphServiceClient = await GetGraphClient();
            Subscription sub = new Subscription();

            // The change type or change types you want to subscribe to 
            sub.ChangeType = "updated";
            
            // IMPORTANT: The notification url ONLY supports the HTTPS protocol
            sub.NotificationUrl = "https://jpowell.azurewebsites.net/api/subscription";
            
            // What resource you want to subscribe to: /users, /groups, etc.
            sub.Resource = "/users";

            // When should this new subscription expire. Note: subscriptions can be renewed using the ID and a different API call
            sub.ExpirationDateTime = DateTime.UtcNow.AddMinutes(10);

            Subscription newSubscription = await graphServiceClient
              .Subscriptions
              .Request()
              .AddAsync(sub);

            // If successful, the new subscription ID And the time of expiration will be returned in the payload
            return $"Subscribed. Id: {newSubscription.Id}, Expiration: {newSubscription.ExpirationDateTime}";
        }

        // POST api/subscription
        public async Task<object> Post([FromUri]string validationToken = null) {
            MySqlConnection connection = new MySqlConnection(ConnectionString);
            connection.Open();

            string insertQuery;

            if (!string.IsNullOrWhiteSpace(validationToken)) {
                insertQuery = $"INSERT INTO junk_container (`id`, `application`, `data`, `insert_date`) " +
                    $"VALUES (NULL, 'Subscription Test App', {JsonConvert.SerializeObject(validationToken)}, CURRENT_TIMESTAMP)";

                new MySqlCommand(insertQuery, connection).ExecuteNonQuery();

                return new HttpResponseMessage() {
                    Content = new StringContent(validationToken, Encoding.UTF8, "text/plain")
                };
            }

            try {
                // handle notifications
                using (StreamReader reader = new StreamReader(await Request.Content.ReadAsStreamAsync())) {
                    string content = await reader.ReadToEndAsync();

                    insertQuery = $"INSERT INTO junk_container (`id`, `application`, `data`, `insert_date`) " +
                        $"VALUES (NULL, 'Subscription Test App', {JsonConvert.SerializeObject(content)}, CURRENT_TIMESTAMP)";

                    new MySqlCommand(insertQuery, connection).ExecuteNonQuery();

                    var notifications = JsonConvert.DeserializeObject<Notifications>(content);

                    foreach (var notification in notifications.Items) {
                        insertQuery = $"INSERT INTO junk_container (`id`, `application`, `data`, `insert_date`) " +
                        $"VALUES (NULL, 'Subscription Test App', {JsonConvert.SerializeObject(notification)}, CURRENT_TIMESTAMP)";

                        new MySqlCommand(insertQuery, connection).ExecuteNonQuery();
                    }
                }
            } catch (Exception e) {
                insertQuery = $"INSERT INTO junk_container (`id`, `application`, `data`, `insert_date`) " +
                    $"VALUES (NULL, 'Subscription Test App', {JsonConvert.SerializeObject(e)}, CURRENT_TIMESTAMP)";

                new MySqlCommand(insertQuery, connection).ExecuteNonQuery();
            }

            if (connection.State == System.Data.ConnectionState.Open) {
                connection.Close();
            }

            return "It worked???";
        }

        private async Task<GraphServiceClient> GetGraphClient() {
            string token = await GetNewToken();
            var graphClient = new GraphServiceClient(new DelegateAuthenticationProvider((requestMessage) => {
                requestMessage
                    .Headers
                    .Authorization = new AuthenticationHeaderValue("bearer", token);

                return Task.FromResult(0);
            }));

            return graphClient;
        }

        private async Task<string> GetNewToken() {
            HttpClient client = new HttpClient();
            string authEndpoint = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token";

            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>();
            pairs.Add(new KeyValuePair<string, string>("grant_type", "client_credentials"));
            pairs.Add(new KeyValuePair<string, string>("client_id", $"{ApplicationId}"));
            pairs.Add(new KeyValuePair<string, string>("client_secret", $"{ApplicationSecret}"));
            pairs.Add(new KeyValuePair<string, string>("scope", "https://graph.microsoft.com/.default"));

            FormUrlEncodedContent content = new FormUrlEncodedContent(pairs);
            HttpResponseMessage response = await client.PostAsync(authEndpoint, content);
            Token data = await response.Content.ReadAsAsync<Token>();

            return data.Value;
        }
    }

    public class Token
    {
        [JsonProperty(PropertyName = "access_token")]
        public string Value { get; set; }
    }
    public class Notifications
    {
        [JsonProperty(PropertyName = "value")]
        public Notification[] Items { get; set; }
    }

    public class ResourceData
    {
        // The ID of the resource.
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }

        // The OData etag property.
        [JsonProperty(PropertyName = "@odata.etag")]
        public string ODataEtag { get; set; }

        // The OData ID of the resource. This is the same value as the resource property.
        [JsonProperty(PropertyName = "@odata.id")]
        public string ODataId { get; set; }

        // The OData type of the resource: "#Microsoft.Graph.Message", "#Microsoft.Graph.Event", or "#Microsoft.Graph.Contact".
        [JsonProperty(PropertyName = "@odata.type")]
        public string ODataType { get; set; }
    }

    // A change notification.
    public class Notification
    {
        // The type of change.
        [JsonProperty(PropertyName = "changeType")]
        public string ChangeType { get; set; }

        // The client state used to verify that the notification is from Microsoft Graph. Compare the value received with the notification to the value you sent with the subscription request.
        [JsonProperty(PropertyName = "clientState")]
        public string ClientState { get; set; }

        // The endpoint of the resource that changed. For example, a message uses the format ../Users/{user-id}/Messages/{message-id}
        [JsonProperty(PropertyName = "resource")]
        public string Resource { get; set; }

        // The UTC date and time when the webhooks subscription expires.
        [JsonProperty(PropertyName = "subscriptionExpirationDateTime")]
        public DateTimeOffset SubscriptionExpirationDateTime { get; set; }

        // The unique identifier for the webhooks subscription.
        [JsonProperty(PropertyName = "subscriptionId")]
        public string SubscriptionId { get; set; }

        // Properties of the changed resource.
        [JsonProperty(PropertyName = "resourceData")]
        public ResourceData ResourceData { get; set; }
    }
}
