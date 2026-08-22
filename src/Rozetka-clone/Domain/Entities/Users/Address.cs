using Domain.Entities.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities.Users
{
    public sealed class Address : BaseEntity
    {
        private Address()
        {
        }

        private Address(
            Guid userId,
            string country,
            string? region,
            string city,
            string street,
            string building,
            string? apartment,
            string? postalCode,
            string recipientName,
            string recipientPhone,
            bool defaultAddress)
        {
            UserId = userId;
            Country = NormalizeRequired(country);
            Region = NormalizeOptional(region);
            City = NormalizeRequired(city);
            Street = NormalizeRequired(street);
            Building = NormalizeRequired(building);
            Apartment = NormalizeOptional(apartment);
            PostalCode = NormalizeOptional(postalCode);
            RecipientName = NormalizeRequired(recipientName);
            RecipientPhone = NormalizeRequired(recipientPhone);
            DefaultAddress = defaultAddress;
        }

        public Guid UserId { get; private set; }

        public string Country { get; private set; } = null!;

        public string? Region { get; private set; }

        public string City { get; private set; } = null!;

        public string Street { get; private set; } = null!;

        public string Building { get; private set; } = null!;

        public string? Apartment { get; private set; }

        public string? PostalCode { get; private set; }

        public string RecipientName { get; private set; } = null!;

        public string RecipientPhone { get; private set; } = null!;

        public bool DefaultAddress { get; private set; }

        public static Address Create(
            Guid userId,
            string country,
            string? region,
            string city,
            string street,
            string building,
            string? apartment,
            string? postalCode,
            string recipientName,
            string recipientPhone,
            bool defaultAddress = false)
        {
            if (userId == Guid.Empty)
            {
                throw new ArgumentException(
                    "User id cannot be empty.",
                    nameof(userId));
            }

            return new Address(
                userId,
                country,
                region,
                city,
                street,
                building,
                apartment,
                postalCode,
                recipientName,
                recipientPhone,
                defaultAddress);
        }

        public void Update(
            string country,
            string? region,
            string city,
            string street,
            string building,
            string? apartment,
            string? postalCode,
            string recipientName,
            string recipientPhone)
        {
            Country = NormalizeRequired(country);
            Region = NormalizeOptional(region);
            City = NormalizeRequired(city);
            Street = NormalizeRequired(street);
            Building = NormalizeRequired(building);
            Apartment = NormalizeOptional(apartment);
            PostalCode = NormalizeOptional(postalCode);
            RecipientName = NormalizeRequired(recipientName);
            RecipientPhone = NormalizeRequired(recipientPhone);
        }

        public void SetAsDefault()
        {
            DefaultAddress = true;
        }

        public void RemoveDefault()
        {
            DefaultAddress = false;
        }

        private static string NormalizeRequired(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            return value.Trim();
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }
}
