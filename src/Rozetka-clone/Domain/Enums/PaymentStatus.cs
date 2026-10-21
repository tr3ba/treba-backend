namespace Domain.Enums;

public enum PaymentStatus {
    CREATED,
    PENDING,
    SUCCEEDED,
    FAILED,
    CANCELLED,
    REFUNDED,
    PARTIALLY_REFUNDED
}