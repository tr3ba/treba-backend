using Application.Abstractions;
using Application.Delivery;
using Contracts.Delivery;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.RateLimiting;

namespace WebApi.Tests
{
    public sealed class ShipmentIdempotencyTests
    {
        [Fact]
        public async Task RepeatedHttpCreate_ReturnsSameShipment_AndCallsProviderOnce()
        {
            var userId = Guid.NewGuid();
            var provider = new CountingDeliveryProvider();

            await using var server = await AdminHttpTests.ServerAsync(services =>
            {
                services.AddRateLimiter(_ => { });
                services.AddScoped<IDeliveryProvider>(_ => provider);
                services.AddScoped<IShipmentConflictDetector, NoConflictDetector>();
                services.AddScoped<IDeliveryService, DeliveryService>();
            });

            using var client = server.GetTestClient();

            client.DefaultRequestHeaders.Add("X-Test-Role", "Customer");
            client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());

            Guid orderId;
            Guid methodId;

            using (var scope = server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var seed = await OperationsTests.SeedAsync(
                    db,
                    customerId: userId
                );

                // Создаём заказ именно для авторизованного пользователя.
                Assert.Equal(userId, seed.Customer.Id);

                var createdOrder = await OperationsTests
                    .Service(db, new OperationsTests.Actor(userId))
                    .CreateOrderAsync(OperationsTests.Request(seed));

                var order = await db.Orders.SingleAsync(x => x.Id == createdOrder.Id);
                order.Status = OrderStatus.READY_FOR_SHIPMENT;

                var method = new DeliveryMethod
                {
                    Id = Guid.NewGuid(),
                    Name = "Test delivery",
                    Code = "TEST",
                    Active = true
                };

                db.DeliveryMethods.Add(method);
                await db.SaveChangesAsync();

                orderId = order.Id;
                methodId = method.Id;
            }

            var path = $"/api/v1/delivery/orders/{orderId}/shipments";
            var request = new Contracts.Delivery.CreateOrderShipmentRequest(methodId);

            using var first = await client.PostAsJsonAsync(path, request);
            using var second = await client.PostAsJsonAsync(path, request);

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);

            var firstShipment = await first.Content.ReadFromJsonAsync<ShipmentDto>();
            var secondShipment = await second.Content.ReadFromJsonAsync<ShipmentDto>();

            Assert.NotNull(firstShipment);
            Assert.NotNull(secondShipment);

            Assert.Equal(firstShipment.Id, secondShipment.Id);
            Assert.Equal(orderId, firstShipment.OrderId);
            Assert.Equal(methodId, firstShipment.DeliveryMethodId);

            Assert.Equal(1, provider.CreateCalls);

            using (var scope = server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Equal(1, await db.Shipments.CountAsync());
            }
        }

        [Fact]
        public async Task RepeatedCreate_ReturnsSameShipment_AndCallsProviderOnce()
        {
            await using var db = TelegramTestData.Memory();

            var seed = await OperationsTests.SeedAsync(db);
            var actor = new OperationsTests.Actor(seed.Customer.Id);

            var createdOrder = await OperationsTests
                .Service(db, actor)
                .CreateOrderAsync(OperationsTests.Request(seed));

            var order = await db.Orders.SingleAsync(x => x.Id == createdOrder.Id);
            order.Status = OrderStatus.READY_FOR_SHIPMENT;

            var method = new DeliveryMethod
            {
                Id = Guid.NewGuid(),
                Name = "Test delivery",
                Code = "TEST",
                Active = true
            };

            db.DeliveryMethods.Add(method);
            await db.SaveChangesAsync();

            var provider = new CountingDeliveryProvider();
            var service = new DeliveryService(
                db,
                provider,
                actor,
                new NoConflictDetector());

            var first = await service.CreateShipmentAsync(order.Id, method.Id);
            var second = await service.CreateShipmentAsync(order.Id, method.Id);

            Assert.Equal(first.Id, second.Id);
            Assert.Equal(1, provider.CreateCalls);
            Assert.Equal(1, await db.Shipments.CountAsync());
            Assert.Equal("CREATED", second.Status);
        }

        [Fact]
        public async Task RepeatedCreate_WithDifferentDeliveryMethod_RejectsWithoutCallingProviderAgain()
        {
            await using var db = TelegramTestData.Memory();

            var seed = await OperationsTests.SeedAsync(db);
            var actor = new OperationsTests.Actor(seed.Customer.Id);

            var createdOrder = await OperationsTests
                .Service(db, actor)
                .CreateOrderAsync(OperationsTests.Request(seed));

            var order = await db.Orders.SingleAsync(x => x.Id == createdOrder.Id);
            order.Status = OrderStatus.READY_FOR_SHIPMENT;

            var firstMethod = new DeliveryMethod
            {
                Id = Guid.NewGuid(),
                Name = "First delivery",
                Code = "FIRST",
                Active = true
            };

            var secondMethod = new DeliveryMethod
            {
                Id = Guid.NewGuid(),
                Name = "Second delivery",
                Code = "SECOND",
                Active = true
            };

            db.DeliveryMethods.AddRange(firstMethod, secondMethod);
            await db.SaveChangesAsync();

            var provider = new CountingDeliveryProvider();
            var service = new DeliveryService(
                db,
                provider,
                actor,
                new NoConflictDetector());

            await service.CreateShipmentAsync(order.Id, firstMethod.Id);

            var exception = await Assert.ThrowsAsync<ShipmentConflictException>(
                () => service.CreateShipmentAsync(order.Id, secondMethod.Id));

            Assert.Contains("другим способом доставки", exception.Message);
            Assert.Equal(1, provider.CreateCalls);
            Assert.Equal(1, await db.Shipments.CountAsync());
        }

        [Fact]
        public async Task RepeatedCreate_WhileCreating_RejectsWithoutCallingProvider()
        {
            await using var db = TelegramTestData.Memory();

            var seed = await OperationsTests.SeedAsync(db);
            var actor = new OperationsTests.Actor(seed.Customer.Id);

            var createdOrder = await OperationsTests
                .Service(db, actor)
                .CreateOrderAsync(OperationsTests.Request(seed));

            var order = await db.Orders.SingleAsync(x => x.Id == createdOrder.Id);
            order.Status = OrderStatus.READY_FOR_SHIPMENT;

            var method = new DeliveryMethod
            {
                Id = Guid.NewGuid(),
                Name = "Test delivery",
                Code = "TEST",
                Active = true
            };

            db.DeliveryMethods.Add(method);

            db.Shipments.Add(new Shipment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                DeliveryMethodId = method.Id,
                Provider = "Test",
                Status = ShipmentStatus.CREATING,
                Price = 0m
            });

            await db.SaveChangesAsync();

            var provider = new CountingDeliveryProvider();
            var service = new DeliveryService(
                db,
                provider,
                actor,
                new NoConflictDetector());

            var exception = await Assert.ThrowsAsync<ShipmentConflictException>(
                () => service.CreateShipmentAsync(order.Id, method.Id));

            Assert.Equal("Создание отправления уже выполняется.", exception.Message);
            Assert.Equal(0, provider.CreateCalls);
            Assert.Equal(1, await db.Shipments.CountAsync());
        }

        [Fact]
        public async Task ProviderFailure_KeepsReservation_AndPreventsDuplicateCall()
        {
            await using var db = TelegramTestData.Memory();

            var seed = await OperationsTests.SeedAsync(db);
            var actor = new OperationsTests.Actor(seed.Customer.Id);

            var createdOrder = await OperationsTests
                .Service(db, actor)
                .CreateOrderAsync(OperationsTests.Request(seed));

            var order = await db.Orders.SingleAsync(x => x.Id == createdOrder.Id);
            order.Status = OrderStatus.READY_FOR_SHIPMENT;

            var method = new DeliveryMethod
            {
                Id = Guid.NewGuid(),
                Name = "Test delivery",
                Code = "TEST",
                Active = true
            };

            db.DeliveryMethods.Add(method);
            await db.SaveChangesAsync();

            var provider = new FailingDeliveryProvider();
            var service = new DeliveryService(
                db,
                provider,
                actor,
                new NoConflictDetector());

            await Assert.ThrowsAsync<TimeoutException>(
                () => service.CreateShipmentAsync(order.Id, method.Id));

            var shipment = await db.Shipments.SingleAsync();

            Assert.Equal(ShipmentStatus.CREATING, shipment.Status);
            Assert.Equal(1, provider.CreateCalls);

            var exception = await Assert.ThrowsAsync<ShipmentConflictException>(
                () => service.CreateShipmentAsync(order.Id, method.Id));

            Assert.Equal("Создание отправления уже выполняется.", exception.Message);
            Assert.Equal(1, provider.CreateCalls);
            Assert.Equal(1, await db.Shipments.CountAsync());
        }

        private sealed class NoConflictDetector : IShipmentConflictDetector
        {
            public bool IsActiveShipmentConflict(Exception exception) => false;
        }

        private sealed class CountingDeliveryProvider : IDeliveryProvider
        {
            public int CreateCalls { get; private set; }

            public Task<CreateShipmentResponse> CreateShipmentAsync(
                CreateShipmentRequest request,
                CancellationToken cancellationToken = default)
            {
                CreateCalls++;

                return Task.FromResult(
                    new CreateShipmentResponse(
                        "TEST-TRACKING-001",
                        50m,
                        DateTimeOffset.UtcNow.AddDays(3)));
            }

            public Task<List<PickupPointDto>> GetPickupPointsAsync(
                string city,
                CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<DeliveryPriceDto> CalculatePriceAsync(
                DeliveryProviderRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<TrackingDto> TrackAsync(
                string trackingNumber,
                CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }

        private sealed class FailingDeliveryProvider : IDeliveryProvider
        {
            public int CreateCalls { get; private set; }

            public Task<CreateShipmentResponse> CreateShipmentAsync(
                CreateShipmentRequest request,
                CancellationToken cancellationToken = default)
            {
                CreateCalls++;
                throw new TimeoutException("Provider response timed out.");
            }

            public Task<List<PickupPointDto>> GetPickupPointsAsync(
                string city,
                CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<DeliveryPriceDto> CalculatePriceAsync(
                DeliveryProviderRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<TrackingDto> TrackAsync(
                string trackingNumber,
                CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }
    }
}
