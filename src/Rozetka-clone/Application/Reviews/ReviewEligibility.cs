using Domain.Enums;

namespace Application.Reviews;

public static class ReviewEligibility
{
    public static bool IsPurchased(OrderStatus status, PaymentStatus paymentStatus) =>
        paymentStatus == PaymentStatus.SUCCEEDED && status is OrderStatus.DELIVERED or OrderStatus.COMPLETED;
}
