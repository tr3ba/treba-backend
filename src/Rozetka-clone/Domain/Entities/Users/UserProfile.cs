using Domain.Entities.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities.Users
{
    public sealed class UserProfile : BaseEntity
    {
        private UserProfile()
        {
        }

        private UserProfile(
            Guid userId,
            DateOnly? birthDate,
            string? gender,
            string? avatarUrl,
            string language,
            bool marketingEmailsEnabled)
        {
            UserId = userId;
            BirthDate = birthDate;
            Gender = NormalizeOptional(gender);
            AvatarUrl = NormalizeOptional(avatarUrl);
            Language = NormalizeLanguage(language);
            MarketingEmailsEnabled = marketingEmailsEnabled;
        }

        public Guid UserId { get; private set; }

        public DateOnly? BirthDate { get; private set; }

        public string? Gender { get; private set; }

        public string? AvatarUrl { get; private set; }

        public string Language { get; private set; } = "uk";

        public bool MarketingEmailsEnabled { get; private set; }

        public static UserProfile Create(
            Guid userId,
            DateOnly? birthDate = null,
            string? gender = null,
            string? avatarUrl = null,
            string language = "uk",
            bool marketingEmailsEnabled = false)
        {
            if (userId == Guid.Empty)
            {
                throw new ArgumentException(
                    "User id cannot be empty.",
                    nameof(userId));
            }

            ValidateBirthDate(birthDate);

            return new UserProfile(
                userId,
                birthDate,
                gender,
                avatarUrl,
                language,
                marketingEmailsEnabled);
        }

        public void Update(
            DateOnly? birthDate,
            string? gender,
            string? avatarUrl,
            string language,
            bool marketingEmailsEnabled)
        {
            ValidateBirthDate(birthDate);

            BirthDate = birthDate;
            Gender = NormalizeOptional(gender);
            AvatarUrl = NormalizeOptional(avatarUrl);
            Language = NormalizeLanguage(language);
            MarketingEmailsEnabled = marketingEmailsEnabled;
        }

        public void ChangeAvatar(string? avatarUrl)
        {
            AvatarUrl = NormalizeOptional(avatarUrl);
        }

        public void ChangeLanguage(string language)
        {
            Language = NormalizeLanguage(language);
        }

        public void SetMarketingEmails(bool enabled)
        {
            MarketingEmailsEnabled = enabled;
        }

        private static void ValidateBirthDate(DateOnly? birthDate)
        {
            if (birthDate is null)
            {
                return;
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            if (birthDate > today)
            {
                throw new ArgumentException(
                    "Birth date cannot be in the future.",
                    nameof(birthDate));
            }
        }

        private static string NormalizeLanguage(string language)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(language);

            return language.Trim().ToLowerInvariant();
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }
}
