using System;

namespace Domain.Entities.Product;

public class ProductVariant
{
    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }

    public string Sku { get; private set; } = string.Empty;
    public string? Barcode { get; private set; }
    public string Name { get; private set; } = string.Empty;

    public decimal Price { get; private set; }
    public decimal? OldPrice { get; private set; }
    public decimal? CostPrice { get; private set; }

    public double? Weight { get; private set; }
    public double? Length { get; private set; }
    public double? Width { get; private set; }
    public double? Height { get; private set; }

    public bool IsActive { get; private set; }

    private ProductVariant()
    {
    }

    public ProductVariant(
        Guid id,
        Guid productId,
        string sku,
        string? barcode,
        string name,
        decimal price,
        decimal? oldPrice,
        decimal? costPrice,
        double? weight,
        double? length,
        double? width,
        double? height,
        bool isActive)
    {
        Id = id;
        ProductId = productId;
        Sku = sku;
        Barcode = barcode;
        Name = name;
        Price = price;
        OldPrice = oldPrice;
        CostPrice = costPrice;
        Weight = weight;
        Length = length;
        Width = width;
        Height = height;
        IsActive = isActive;
    }

    public void Update(
        string sku,
        string? barcode,
        string name,
        decimal price,
        decimal? oldPrice,
        decimal? costPrice,
        double? weight,
        double? length,
        double? width,
        double? height,
        bool isActive)
    {
        Sku = sku;
        Barcode = barcode;
        Name = name;
        Price = price;
        OldPrice = oldPrice;
        CostPrice = costPrice;
        Weight = weight;
        Length = length;
        Width = width;
        Height = height;
        IsActive = isActive;
    }
}