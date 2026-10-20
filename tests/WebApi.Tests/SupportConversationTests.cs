using System.Net;
using System.Net.Http.Json;
using Application.Administration;
using Application.Common;
using Contracts.Admin.Operations;
using Domain.Entities.Support;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace WebApi.Tests;

public sealed class SupportConversationTests
{
    private static SupportService Service(ApplicationDbContext db, Guid customer, string role = "Customer")
    { var actor = new OperationsTests.Actor(customer, role); return new(db, actor, OperationsTests.Service(db, actor)); }

    [Fact]
    public async Task PersonalChat_DoesNotNeedAnOrder_RetriesDoNotDuplicate_AndNotesRemainPrivate()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db);
        var customer = Service(db, seed.Customer.Id); var manager = Service(db, Guid.NewGuid(), "Manager");
        var request = new CreateTicketRequest { Kind = "CHAT", Subject = "Чат з TREBA", Message = "Help me choose", ClientRequestId = Guid.NewGuid() };
        var chat = await customer.CreateAsync(request, default);
        Assert.Null(chat.Summary.OrderId); Assert.Null(chat.Summary.ProductId);
        Assert.Equal(chat.Summary.Id, (await customer.CreateAsync(request, default)).Summary.Id); Assert.Single(await db.SupportMessages.ToListAsync());
        var followup = await customer.CreateAsync(new() { Kind = "CHAT", Subject = "Chat again", Message = "One more question", ClientRequestId = Guid.NewGuid() }, default);
        Assert.Equal(chat.Summary.Id, followup.Summary.Id); Assert.Single(await db.SupportTickets.ToListAsync());
        var inbox = Assert.Single((await manager.ListAsync(1, null, "CHAT", null, default, "unread")).Content); Assert.Equal(2, inbox.UnreadCount);
        await manager.MarkReadAsync(chat.Summary.Id, new(followup.Messages.Last().Id), default);
        Assert.Empty((await manager.ListAsync(1, null, null, null, default, "unread")).Content);
        await manager.MessageAsync(chat.Summary.Id, new("Confidential internal note", true), default);
        Assert.DoesNotContain("Confidential", (await customer.ListAsync(1, null, null, null, default)).Content.Single().LastMessage);
        Assert.Equal(0, (await customer.DetailsAsync(chat.Summary.Id, default))!.Summary.UnreadCount);
        var message = new TicketMessageRequest("We can help", false, Guid.NewGuid());
        await manager.MessageAsync(chat.Summary.Id, message, default); await manager.MessageAsync(chat.Summary.Id, message, default);
        var answered = (await customer.DetailsAsync(chat.Summary.Id, default))!;
        Assert.Equal(3, answered.Messages.Count); Assert.Equal(1, answered.Summary.UnreadCount); Assert.Equal("IN_PROGRESS", answered.Summary.Status);
        await customer.MarkReadAsync(chat.Summary.Id, new(answered.Messages.Last().Id), default);
        var read = (await customer.DetailsAsync(chat.Summary.Id, default))!; Assert.Equal(0, read.Summary.UnreadCount); Assert.Equal(answered.Summary.Version, read.Summary.Version); Assert.Equal(answered.Summary.UpdatedAt, read.Summary.UpdatedAt);
        var stranger = Service(db, Guid.NewGuid()); Assert.Null(await stranger.DetailsAsync(chat.Summary.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => stranger.MessagesAsync(chat.Summary.Id, null, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => stranger.MessageAsync(chat.Summary.Id, new("Intrusion"), default));
        var noteId = (await db.SupportMessages.SingleAsync(m => m.Internal)).Id;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => customer.MarkReadAsync(chat.Summary.Id, new(noteId), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => customer.MessagesAsync(chat.Summary.Id, noteId, default));
        Assert.Single((await new OperationsService(db).CustomerAsync(seed.Customer.Id, default))!.Tickets);
        Assert.Single((await new OperationsService(db).CustomersAsync(1, seed.Customer.Email, default)).Content);
    }

    [Fact]
    public async Task ProductQuestion_IsPrivateAndReusesOnlyTheSameCustomersActiveProductThread()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db); var customer = Service(db, seed.Customer.Id);
        var request = new CreateTicketRequest { Kind = "QUESTION", ProductId = seed.Product.Id, Subject = "Product question", Message = "What size?" };
        var question = await customer.CreateAsync(request, default); Assert.Equal(seed.Product.Name, question.Summary.ProductName); Assert.Equal(seed.Product.Slug, question.Summary.ProductSlug); Assert.Null(question.Summary.OrderId);
        Assert.Equal(question.Summary.Id, (await customer.CreateAsync(request, default)).Summary.Id);
        var other = await TelegramTestData.UserAsync(db, "Customer"); var otherService = Service(db, other.Id);
        var another = await otherService.CreateAsync(request, default); Assert.NotEqual(question.Summary.Id, another.Summary.Id);
        Assert.Single((await otherService.ListAsync(1, null, null, seed.Product.Name, default, productId: seed.Product.Id)).Content);
        var manager = Service(db, Guid.NewGuid(), "Manager");
        await manager.ChangeStatusAsync(question.Summary.Id, new("CLOSED", (await manager.DetailsAsync(question.Summary.Id, default))!.Summary.Version), default);
        await Assert.ThrowsAsync<BusinessRuleException>(() => customer.MessageAsync(question.Summary.Id, new("After close"), default));
        Assert.NotEqual(question.Summary.Id, (await customer.CreateAsync(request, default)).Summary.Id);
        seed.Product.Status = ProductStatus.ARCHIVED; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => customer.CreateAsync(request, default));
        await Assert.ThrowsAsync<ArgumentException>(() => customer.CreateAsync(new() { Kind = "RETURN", Subject = "Return", Message = "No order" }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => otherService.MessageAsync(another.Summary.Id, new("Secret", true), default));
    }

    [Fact]
    public async Task HistoryPagination_HasNoGapsOrDuplicates_AndInternalNotesDoNotCountAsAnAnswer()
    {
        await using var db = TelegramTestData.Memory(); var seed = await OperationsTests.SeedAsync(db); var customer = Service(db, seed.Customer.Id); var manager = Service(db, Guid.NewGuid(), "Manager");
        var chat = await customer.CreateAsync(new() { Kind = "CHAT", Subject = "History", Message = "Start" }, default);
        var ticket = await db.SupportTickets.SingleAsync(); ticket.CreatedAt = DateTimeOffset.UtcNow.AddDays(-3);
        for(var i=0;i<105;i++) db.SupportMessages.Add(new() { TicketId=chat.Summary.Id,AuthorId=seed.Customer.Id,AuthorName="Fixture",AuthorRole="Customer",Message="Message "+i,CreatedAt=DateTimeOffset.UtcNow.AddMinutes(-110+i) });
        await db.SaveChangesAsync(); await manager.MessageAsync(chat.Summary.Id, new("Internal investigation",true),default);
        Assert.Single((await manager.ListAsync(1,null,null,null,default,"overdue")).Content);
        var details=(await customer.DetailsAsync(chat.Summary.Id,default))!; Assert.Equal(50,details.Messages.Count); Assert.True(details.HasEarlierMessages);
        var older=await customer.MessagesAsync(chat.Summary.Id,details.Messages[0].Id,default); Assert.Equal(50,older.Messages.Count); Assert.True(older.HasMore);
        var oldest=await customer.MessagesAsync(chat.Summary.Id,older.Messages[0].Id,default); Assert.Equal(6,oldest.Messages.Count); Assert.False(oldest.HasMore);
        Assert.Equal(106,details.Messages.Concat(older.Messages).Concat(oldest.Messages).Select(m=>m.Id).Distinct().Count());
    }

    [Fact]
    public async Task ConversationHttp_EnforcesOwnershipRolesValidationAndReadMarkers()
    {
        await using var app=await AdminHttpTests.ServerAsync(); Guid customerId;
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();customerId=(await OperationsTests.SeedAsync(db)).Customer.Id;}
        using var client=app.GetTestClient(); Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/support")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Role","Customer");client.DefaultRequestHeaders.Add("X-Test-UserId",customerId.ToString());
        using var created=await client.PostAsJsonAsync("/api/v1/support",new CreateTicketRequest {Kind="CHAT",Subject="Chat",Message="Help",ClientRequestId=Guid.NewGuid()});Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var details=(await created.Content.ReadFromJsonAsync<TicketDetails>())!;
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync($"/api/v1/support/{details.Summary.Id}/messages",new {message=" "})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync($"/api/v1/support/{details.Summary.Id}/read",new ReadTicketRequest(details.Messages[0].Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PatchAsJsonAsync($"/api/v1/support/{details.Summary.Id}/status",new ChangeTicketStatusRequest("CLOSED",details.Summary.Version))).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-UserId");client.DefaultRequestHeaders.Add("X-Test-UserId",Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/support/{details.Summary.Id}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsJsonAsync($"/api/v1/support/{details.Summary.Id}/read",new ReadTicketRequest(details.Messages[0].Id))).StatusCode);
    }

    private static readonly string Prefix="treba_qa_chat_"+Guid.NewGuid().ToString("N")[..10]+"_";
    public sealed class ChatModel(ModelCustomizerDependencies dependencies):ModelCustomizer(dependencies)
    {public override void Customize(ModelBuilder builder,DbContext context){base.Customize(builder,context);foreach(var entity in builder.Model.GetEntityTypes())if(entity.GetTableName() is {} table){entity.SetTableName(Prefix+table);entity.SetSchema("public");}}}
    [PostgresFact]
    public async Task PostgreSql_ConversationQueriesHistoryReadMarkersAndUniqueActiveThreads()
    {
        var connection=Environment.GetEnvironmentVariable("TREBA_TEST_POSTGRES")!;var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).ReplaceService<IModelCustomizer,ChatModel>().Options;
        await using var sql=new NpgsqlConnection(connection);await sql.OpenAsync();
        async Task Execute(string statement){await using var command=new NpgsqlCommand(statement,sql);await command.ExecuteNonQueryAsync();}
        string[] tables=[];
        try {
            await using var db=new ApplicationDbContext(options);tables=db.Model.GetEntityTypes().Select(e=>e.GetTableName()!).Distinct().ToArray();Assert.All(tables,t=>Assert.StartsWith(Prefix,t));await Execute(db.Database.GenerateCreateScript());
            var seed=await OperationsTests.SeedAsync(db);var customer=Service(db,seed.Customer.Id);var manager=Service(db,Guid.NewGuid(),"Manager");
            var chat=await customer.CreateAsync(new(){Kind="CHAT",Subject="Postgres chat",Message="Hello"},default);
            for(var i=0;i<55;i++)await customer.MessageAsync(chat.Summary.Id,new("Line "+i),default);
            var list=await manager.ListAsync(1,null,null,"Postgres",default,"unread");Assert.Single(list.Content);Assert.Equal(56,list.Content[0].UnreadCount);
            var details=(await manager.DetailsAsync(chat.Summary.Id,default))!;var older=await manager.MessagesAsync(chat.Summary.Id,details.Messages[0].Id,default);Assert.Equal(6,older.Messages.Count);
            await manager.MarkReadAsync(chat.Summary.Id,new(details.Messages.Last().Id),default);Assert.Empty((await manager.ListAsync(1,null,null,null,default,"unread")).Content);
            var question=await customer.CreateAsync(new(){Kind="QUESTION",ProductId=seed.Product.Id,Subject="Size",Message="Which size?"},default);
            Assert.Equal(seed.Product.Name,question.Summary.ProductName);Assert.Single((await customer.ListAsync(1,null,null,seed.Product.Name,default)).Content);
            Assert.Equal(2,(await new OperationsService(db).CustomerAsync(seed.Customer.Id,default))!.Tickets.Count);
            db.SupportTickets.Add(new(){CustomerId=seed.Customer.Id,Kind="CHAT",Subject="Concurrent duplicate"});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
        } finally {foreach(var table in tables.Reverse()){Assert.StartsWith(Prefix,table);await Execute($"DROP TABLE IF EXISTS public.\"{table}\" CASCADE");}}
    }
}
