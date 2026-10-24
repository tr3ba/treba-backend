using Contracts.Admin.Operations;
using Contracts.Common;

namespace AdminPanel.Services.Implementations;

public sealed class OperationsApiClient(HttpClient http)
{
    public Task<TicketCapabilities> TicketCapabilities() => Get<TicketCapabilities>("api/v1/support/capabilities");
    public Task<PagedResponse<TicketRow>> DeletedTickets(int page, string? search) => Get<PagedResponse<TicketRow>>($"api/v1/support/deleted?page={page}&search={E(search)}");
    public Task DeleteTicket(Guid id, Guid version) => Send(HttpMethod.Delete, $"api/v1/support/{id}", new TicketDeletionRequest(version));
    public Task RestoreTicket(Guid id, Guid version) => Send(HttpMethod.Post, $"api/v1/support/{id}/restore", new TicketDeletionRequest(version));
    public Task<PagedResponse<OrderRow>> Orders(int page = 1, string? status = null, string? search = null, DateTimeOffset? from = null, DateTimeOffset? to = null, Guid? customerId = null) =>
        Get<PagedResponse<OrderRow>>($"api/v1/admin/orders?page={page}&size=20&status={E(status)}&search={E(search)}&from={E(from?.ToString("O"))}&to={E(to?.ToString("O"))}&customerId={customerId}");
    public Task<OrderDetails> Order(Guid id) => Get<OrderDetails>($"api/v1/admin/orders/{id}");
    public Task ChangeOrder(Guid id, ChangeOrderStatusRequest request) => Send(HttpMethod.Patch, $"api/v1/admin/orders/{id}/status", request);
    public Task<PagedResponse<PromotionView>> Promotions(int page, string? search, string? status) => Get<PagedResponse<PromotionView>>($"api/v1/admin/promotions?page={page}&search={E(search)}&status={E(status)}");
    public Task<PromotionView> Promotion(Guid id) => Get<PromotionView>($"api/v1/admin/promotions/{id}");
    public async Task<PromotionView> SavePromotion(Guid? id, PromotionInput request)
    {
        using var response = id.HasValue ? await http.PutAsJsonAsync($"api/v1/admin/promotions/{id}", request) : await http.PostAsJsonAsync("api/v1/admin/promotions", request);
        await CatalogApiClient.RequireSuccess(response);
        return await Read<PromotionView>(response);
    }
    public Task<PagedResponse<TicketRow>> Tickets(int page, string? status, string? kind, string? search, string? view = null) => Get<PagedResponse<TicketRow>>($"api/v1/support?page={page}&status={E(status)}&kind={E(kind)}&search={E(search)}&view={E(view)}");
    public Task<TicketDetails> Ticket(Guid id) => Get<TicketDetails>($"api/v1/support/{id}");
    public Task<TicketMessagePage> TicketMessages(Guid id, Guid before) => Get<TicketMessagePage>($"api/v1/support/{id}/messages?before={before}");
    public Task ReadTicket(Guid id, Guid messageId) => Send(HttpMethod.Post, $"api/v1/support/{id}/read", new ReadTicketRequest(messageId));
    public async Task<TicketDetails> CreateTicket(CreateTicketRequest request)
    { using var response = await http.PostAsJsonAsync("api/v1/support", request); await CatalogApiClient.RequireSuccess(response); return await Read<TicketDetails>(response); }
    public Task Message(Guid id, TicketMessageRequest request) => Send(HttpMethod.Post, $"api/v1/support/{id}/messages", request);
    public Task ChangeTicket(Guid id, ChangeTicketStatusRequest request) => Send(HttpMethod.Patch, $"api/v1/support/{id}/status", request);
    public Task<PagedResponse<CustomerRow>> Customers(int page, string? search) => Get<PagedResponse<CustomerRow>>($"api/v1/admin/operations/customers?page={page}&search={E(search)}");
    public Task<CustomerDetails> Customer(Guid id) => Get<CustomerDetails>($"api/v1/admin/operations/customers/{id}");
    public Task<OperationsMetrics> Metrics(int days) => Get<OperationsMetrics>($"api/v1/admin/operations/metrics?days={days}");
    public async Task<T> Get<T>(string url)
    { using var response = await http.GetAsync(url); await CatalogApiClient.RequireSuccess(response); return await Read<T>(response); }
    private async Task Send<T>(HttpMethod method, string url, T body)
    { using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) }; using var response = await http.SendAsync(request); await CatalogApiClient.RequireSuccess(response); }
    private static async Task<T> Read<T>(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<T>() ?? throw new HttpRequestException("API returned an empty response.");
    private static string E(string? text) => Uri.EscapeDataString(text ?? "");
}

public static class OperationsUi
{
    public static readonly string[] OrderStatuses = ["CREATED", "AWAITING_PAYMENT", "PAID", "CONFIRMED", "PROCESSING", "READY_FOR_SHIPMENT", "SHIPPED", "DELIVERED", "COMPLETED", "CANCELLED", "RETURN_REQUESTED", "RETURNED"];
    public static readonly string[] PromotionStatuses = ["DRAFT", "SCHEDULED", "ACTIVE", "FINISHED", "CANCELLED"];
    public static readonly string[] TicketStatuses = ["OPEN", "IN_PROGRESS", "APPROVED", "REJECTED", "CLOSED"];
}
