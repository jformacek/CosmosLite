using Microsoft.Identity.Client;
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace GreyCorbel.Identity.Authentication
{
    /// <summary>
    /// Main object responsible for authentication according to constructor and parameters used
    /// </summary>
    public class AadAuthenticationFactory
    {
        /// <summary>
        /// Tenant Id of AAD tenant that authenticates the user / app
        /// </summary>
        public string TenantId { get { return _tenantId; } }
        private readonly string _tenantId;
        /// <summary>
        /// ClientId to be used for authentication flows
        /// </summary>
        public string ClientId {get {return _clientId;}}
        private readonly string _clientId;
        /// <summary>
        /// AAD authorization endpoint. Defaults to public AAD
        /// </summary>
        public string LoginApi {get {return _loginApi;}}
        private readonly string _loginApi;

        /// <summary>
        /// Scopes the factory asks for when asking for tokens
        /// </summary>
        public string[] Scopes {get {return _scopes;}}
        private readonly string[] _scopes;
        
        /// <summary>
        /// Authentication mode for public client flows
        /// </summary>
        public AuthenticationMode AuthMode { get {return _authMode;}}
        private readonly AuthenticationMode _authMode;
        private readonly AuthenticationFlow _flow;

        /// <summary>
        /// UserName hint to use in authentication flows to help select proper user. Useful in case multiple accounts are logged in.
        /// </summary>
        public string UserName { get { return _userNameHint; } }
        private readonly string _userNameHint;

        private readonly IPublicClientApplication _publicClientApplication;
        private readonly IConfidentialClientApplication _confidentialClientApplication;
        private readonly ManagedIdentityClientApplication _managedIdentityClientApplication;
        /// <summary>
        /// Creates factory that supporrts Public client flows with Interactive or DeviceCode authentication
        /// </summary>
        /// <param name="tenantId">DNS name or Id of tenant that authenticates user</param>
        /// <param name="clientId">ClientId to use</param>
        /// <param name="scopes">List of scopes that clients asks for</param>
        /// <param name="loginApi">AAD endpoint that will handle the authentication.</param>
        /// <param name="authenticationMode">Type of public client flow to use</param>
        /// <param name="userNameHint">Which username to use in auth UI in case there may be multiple names available</param>
        public AadAuthenticationFactory(
            string tenantId, 
            string clientId, 
            string [] scopes, 
            string loginApi = "https://login.microsoftonline.com", 
            AuthenticationMode authenticationMode = AuthenticationMode.Interactive, 
            string userNameHint = null)
        {
            _clientId = clientId;
            _loginApi = loginApi;
            _scopes = scopes;
            _authMode = authenticationMode;
            _userNameHint = userNameHint;
            _tenantId = tenantId;

            _flow = AuthenticationFlow.PublicClient;

            var builder = PublicClientApplicationBuilder.Create(_clientId)
                .WithDefaultRedirectUri()
                .WithAuthority($"{_loginApi}/{tenantId}")
                .WithHttpClientFactory(new GcMsalHttpClientFactory());
            

            _publicClientApplication = builder.Build();
        }

        /// <summary>
        /// Creates factory that supporrts Confidential client flows via MSAL with ClientSecret authentication
        /// </summary>
        /// <param name="tenantId">DNS name or Id of tenant that authenticates user</param>
        /// <param name="clientId">ClientId to use</param>
        /// <param name="scopes">List of scopes that clients asks for</param>
        /// <param name="loginApi">AAD endpoint that will handle the authentication.</param>
        /// <param name="clientSecret">Client secret to be used</param>
        public AadAuthenticationFactory(
            string tenantId,
            string clientId,
            string clientSecret,
            string[] scopes,
            string loginApi = "https://login.microsoftonline.com")
        {
            _clientId = clientId;
            _loginApi = loginApi;
            _scopes = scopes;

            _flow = AuthenticationFlow.ConfidentialClient;


            var builder = ConfidentialClientApplicationBuilder.Create(_clientId)
                .WithClientSecret(clientSecret)
                .WithAuthority($"{_loginApi}/{tenantId}")
                .WithHttpClientFactory(new GcMsalHttpClientFactory());

            _confidentialClientApplication = builder.Build();
        }

        /// <summary>
        /// Constructor for Confidential client authentication flow via MSAL and X509 certificate authentication
        /// </summary>
        /// <param name="tenantId">Dns domain name or tenant guid</param>
        /// <param name="clientId">Client id that represents application asking for token</param>
        /// <param name="clientCertificate">X509 certificate with private key. Public part of certificate is expected to be registered with app registration for given client id in AAD.</param>
        /// <param name="scopes">Scopes application asks for</param>
        /// <param name="loginApi">AAD endpoint URL for special instance of AAD (/e.g. US Gov)</param>
        public AadAuthenticationFactory(
            string tenantId,
            string clientId,
            X509Certificate2 clientCertificate,
            string[] scopes,
            string loginApi = "https://login.microsoftonline.com")
        {
            _clientId = clientId;
            _loginApi = loginApi;
            _scopes = scopes;

            _flow = AuthenticationFlow.ConfidentialClient;

            var builder = ConfidentialClientApplicationBuilder.Create(_clientId)
                .WithCertificate(clientCertificate)
                .WithAuthority($"{_loginApi}/{tenantId}");

            _confidentialClientApplication = builder.Build();
        }

        /// <summary>
        /// Creates factory that supports ManagedIdentity authentication
        /// </summary>
        /// <param name="scopes">Required scopes to obtain. Currently obtains all assigned scopes for first resource in the array of scopes.</param>
        public AadAuthenticationFactory(string[] scopes)
        {
            _scopes = scopes;
            _managedIdentityClientApplication = new ManagedIdentityClientApplication(new GcMsalHttpClientFactory());
            _flow = AuthenticationFlow.ManagedIdentity;

        }

        /// <summary>
        /// Creates factory that supports UserAssignedIdentity authentication with provided client id
        /// </summary>
        /// <param name="clientId">AppId of User Assigned Identity</param>
        /// <param name="scopes">Required scopes to obtain. Currently obtains all assigned scopes for first resource in the array.</param>
        public AadAuthenticationFactory(string clientId, string[] scopes)
        {
            _scopes = scopes;
            _clientId = clientId;
            _managedIdentityClientApplication = new ManagedIdentityClientApplication(new GcMsalHttpClientFactory(), clientId);
            _flow = AuthenticationFlow.UserAssignedIdentity;
        }

        /// <summary>
        /// Returns authentication result
        /// Microsoft says we should not instantiate directly - but how to achieve unified experience of caller without being able to return it?
        /// </summary>
        /// <returns cref="AuthenticationResult">Authentication result object either returned fropm MSAL libraries, or - for ManagedIdentity - constructed from Managed Identity endpoint response, as returned by cref="ManagedIdentityClientApplication.ApiVersion" version of endpoint</returns>
        /// <exception cref="ArgumentException">Throws if unsupported authentication mode or flow detected</exception>
        public async Task<AuthenticationResult> AuthenticateAsync()
        {
            using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
            AuthenticationResult result;
            switch(_flow)
            {
                //public client flow
                case AuthenticationFlow.PublicClient:
                    var accounts = await _publicClientApplication.GetAccountsAsync();
                    IAccount account;
                    if (string.IsNullOrWhiteSpace(_userNameHint))
                        account = accounts.FirstOrDefault();
                    else
                        account = accounts.Where(x => string.Compare(x.Username, _userNameHint, true) == 0).FirstOrDefault();

                    try
                    {
                        result = await _publicClientApplication.AcquireTokenSilent(_scopes, account)
                                          .ExecuteAsync(cts.Token);
                    }
                    catch (MsalUiRequiredException)
                    {
                        result = _authMode switch
                        {
                            AuthenticationMode.Interactive => await _publicClientApplication.AcquireTokenInteractive(_scopes).ExecuteAsync(cts.Token),
                            AuthenticationMode.DeviceCode => await _publicClientApplication.AcquireTokenWithDeviceCode(_scopes, callback =>
                            {
                                Console.WriteLine(callback.Message);
                                return Task.FromResult(0);
                            }).ExecuteAsync(cts.Token),
                            _ => throw new ArgumentException($"Unsupported Public client authentication mode: {_authMode}"),
                        };
                    }
                    return result;

                case AuthenticationFlow.ConfidentialClient:
                    return await _confidentialClientApplication.AcquireTokenForClient(_scopes).ExecuteAsync(cts.Token);
                case AuthenticationFlow.ManagedIdentity:
                    return await _managedIdentityClientApplication.AcquireTokenForClientAsync(_scopes, cts.Token);
                case AuthenticationFlow.UserAssignedIdentity:
                    return await _managedIdentityClientApplication.AcquireTokenForClientAsync(_scopes, cts.Token);

            }

            throw new ArgumentException($"Unsupported authentication flow: {_flow}");
        }
    }
}
