using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Security.Authentication.Web;
using Windows.Security.Credentials;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace OAuth2Concur
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        /// <summary>
        /// A credential locker to securely store our Access Token 
        /// http://msdn.microsoft.com/en-us/library/windows/apps/br227089.aspx
        /// </summary>
        private PasswordVault _vault;
        private const string RESOURCE_NAME = "ConcurAccessToken";
        private const string USER = "user";

        // we use a property instead of normal field because of how PasswordVault works
        private string _accessToken
        {
            get
            {
                try
                {
                    var creds = _vault.FindAllByResource(RESOURCE_NAME).FirstOrDefault();
                    if (creds != null)
                    {
                        return _vault.Retrieve(RESOURCE_NAME, USER).Password;
                    }
                    else
                    {
                        return null;
                    }
                }
                catch (Exception)
                {
                    // if no access token found, the FindAllByResource method throws System.Exception: Element not found
                    return null;
                }
            }
            set
            {
                _vault.Add(new PasswordCredential(RESOURCE_NAME, USER, value));
            }
        }

        private string _logText;

        //TODO: Get your Consumer key and Secret - http://ismaelc.github.io/ConcurDisrupt/#getstarted
        private string _consumerKey = "<insert your Consumer Key>";
        private string _consumerSecretKey = "<insert your Secret Key>";
        private string _callbackUrl = "http://www.buildwindows.com";

        /// <summary>
        /// In this sample, we want to see get permissions for USER and EXPENSE REPORT
        /// https://developer.concur.com/api-documentation/oauth-20-0#webstep
        /// </summary>
        private string _scope = "USER,EXPRPT"; 
        private string _authorizationCode;

        public MainPage()
        {
            this.InitializeComponent();

            _vault = new PasswordVault();

            sendHttpRequestButton.Click += sendHttpRequestButton_Click;
            clearLogButton.Click += clearLogButton_Click;
            clearAccessTokenButton.Click += clearAccessToken_Click;
            getAccessTokenButton.Click += getAccessTokenButton_Click;
        }

        async void getAccessTokenButton_Click(object sender, RoutedEventArgs e)
        {
            await checkAndGetAccessToken();
        }

        void clearAccessToken_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var creds = _vault.FindAllByResource(RESOURCE_NAME).FirstOrDefault();
                if (creds != null)
                {
                    _vault.Remove(creds);
                    sendHttpRequestButton.IsEnabled = false;
                }
            }
            catch (Exception)
            {
                //
            }
        }

        void clearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _logText = "";
            logTextBox.Text = "";
        }

        async void sendHttpRequestButton_Click(object sender, RoutedEventArgs e)
        {
            var apiUrl = linkedInApiUrl.Text;
            var url = apiUrl;

            if (!string.IsNullOrEmpty(apiQuery.Text))
            {
                url += "&" + apiQuery.Text;
            }

            using (var httpClient = new HttpClient())
            {
                httpClient.MaxResponseContentBufferSize = int.MaxValue;
                httpClient.DefaultRequestHeaders.ExpectContinue = false;
                httpClient.DefaultRequestHeaders.Add("Authorization", "OAuth " + _accessToken);

                var httpRequestMessage = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri(url)
                };


                var response = await httpClient.SendAsync(httpRequestMessage);

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    log(jsonString);
                }
                else
                {
                    log(response.ToString());
                }
            }
        }

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached.  The Parameter
        /// property is typically used to configure the page.</param>
        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            await checkAndGetAccessToken();
        }

        private async Task checkAndGetAccessToken()
        {
            // If we don't have an access token, we will try to get one
            if (string.IsNullOrEmpty(_accessToken))
            {
                await getAuthorizeCode();
                await getAccessToken();
            }
            sendHttpRequestButton.IsEnabled = true;
            log("Access Token is found, ready to send Concur request...");
        }

        private async Task getAuthorizeCode()
        {
            var url = "https://www.concursolutions.com/net2/oauth2/Login.aspx?"
                                            + "&client_id=" + _consumerKey
                                            + "&scope=" + Uri.EscapeDataString(_scope)
                                            + "&state=STATE"
                                            + "&redirect_uri=" + Uri.EscapeDataString(_callbackUrl);
            log(url);
            var startUri = new Uri(url);
            var endUri = new Uri(_callbackUrl);

            WebAuthenticationResult war = await WebAuthenticationBroker.AuthenticateAsync(
                                                        WebAuthenticationOptions.None,
                                                        startUri,
                                                        endUri);
            switch (war.ResponseStatus)
            {
                case WebAuthenticationStatus.Success:
                    {
                        // grab access_token and oauth_verifier
                        var response = war.ResponseData;
                        IDictionary<string, string> keyDictionary = new Dictionary<string, string>();
                        var qSplit = response.Split('?');
                        foreach (var kvp in qSplit[qSplit.Length - 1].Split('&'))
                        {
                            var kvpSplit = kvp.Split('=');
                            if (kvpSplit.Length == 2)
                            {
                                keyDictionary.Add(kvpSplit[0], kvpSplit[1]);
                            }
                        }

                        _authorizationCode = keyDictionary["code"];
                        break;
                    }
                case WebAuthenticationStatus.UserCancel:
                    {
                        log("HTTP Error returned by AuthenticateAsync() : " + war.ResponseErrorDetail.ToString());
                        break;
                    }
                default:
                case WebAuthenticationStatus.ErrorHttp:
                    log("Error returned by AuthenticateAsync() : " + war.ResponseStatus.ToString());
                    break;
            }
        }

        private async Task getAccessToken()
        {
            var url = "https://www.concursolutions.com/net2/oauth2/GetAccessToken.ashx?"
                + "&code=" + _authorizationCode
                + "&redirect_uri=" + Uri.EscapeDataString(_callbackUrl)
                + "&client_id=" + _consumerKey
                + "&client_secret=" + _consumerSecretKey;

            using (var httpClient = new HttpClient())
            {
                httpClient.MaxResponseContentBufferSize = int.MaxValue;
                httpClient.DefaultRequestHeaders.ExpectContinue = false;

                var httpRequestMessage = new HttpRequestMessage
                {
                    //Method = HttpMethod.Post,
                    Method = HttpMethod.Get,
                    RequestUri = new Uri(url)
                };


               var response = await httpClient.SendAsync(httpRequestMessage);
                //var response = await httpClient.GetAsync(httpRequestMessage);
                var xmlString = await response.Content.ReadAsStringAsync();

                var xml = XDocument.Parse(xmlString);
                var element = getElement(xml, "Token");
                if (element != null)
                {
                    _accessToken = element.Value;
                };

                log("Getting New Access Token");
            }
        }

        // retrieve a single element by tag name
        private XElement getElement(XDocument xmlDocument, string elementName)
        {
            var element = xmlDocument.Descendants("Access_Token").Elements().Where(x => x.Name == elementName).FirstOrDefault();
            return element != null ? element : null;
        }

        private void log(string text)
        {
            _logText += string.Format("\r\n{0}:\t{1}\r\n", DateTime.Now.ToLocalTime(), text);
            logTextBox.Text = _logText;
        }
    }
}
