namespace Domain.Enums;

public enum OrderStatus {
    CREATED,
    AWAITING_PAYMENT,
    PAID,
    CONFIRMED,
    PROCESSING,
    READY_FOR_SHIPMENT,
    SHIPPED,
    DELIVERED,
    COMPLETED,
    CANCELLED,
    RETURN_REQUESTED,
    RETURNED
}