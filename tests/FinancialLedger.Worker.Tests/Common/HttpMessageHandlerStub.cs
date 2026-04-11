using System.Net;
using System.Net.Http;
using System.Text;

namespace FinancialLedger.Worker.Tests.Common;

internal sealed class HttpMessageHandlerStub : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _content;

    public HttpMessageHandlerStub(HttpStatusCode statusCode, string content)
    {
        _statusCode = statusCode;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_content, Encoding.UTF8, "application/json")
        };

        return Task.FromResult(response);
    }
}
