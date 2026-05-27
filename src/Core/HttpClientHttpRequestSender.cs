// -----------------------------------------------------------------------
// <copyright file="HttpClientHttpRequestSender.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.Core
{
    using System;
    using System.Net.Http;
    using System.Net.Security;
    using System.Threading;
    using System.Threading.Tasks;

    public class HttpClientHttpRequestSender : IHttpRequestSender, IDisposable
    {
        private readonly HttpClientHandler httpClientHandler;
        private readonly HttpClient httpClient;

        public HttpClientHttpRequestSender(Func<bool> allowUntrustedCertificates)
        {
            _ = allowUntrustedCertificates ?? throw new ArgumentNullException(nameof(allowUntrustedCertificates));

            this.httpClientHandler = new HttpClientHandler()
            {
                // POS systems (e.g. Verifone Commander) commonly use self-signed, IP-only
                // certificates. Validate normally unless the user has explicitly opted in
                // to allow untrusted certificates (the callback is evaluated per request,
                // so the setting takes effect without restarting the app).
                ServerCertificateCustomValidationCallback = (_, _, _, sslPolicyErrors) =>
                    sslPolicyErrors == SslPolicyErrors.None || allowUntrustedCertificates(),
            };

            this.httpClient = new HttpClient(this.httpClientHandler);
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return this.httpClient.SendAsync(request, cancellationToken);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            this.httpClientHandler.Dispose();
            this.httpClient.Dispose();
        }
    }
}
