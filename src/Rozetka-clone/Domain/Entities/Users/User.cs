using Domain.Entities.Common;
using System.Linq;

namespace Domain.Entities.Users
{
    public sealed class User : AggregateRoot
    {
        private User()
        {
        }

        private User(
            Guid id,
            string email,
            string? phone,
            string firstName,
            string lastName,
            string? middleName)
        {
            Id = id;
            Email = email;
            Phone = phone;
            FirstName = firstName;
            LastName = lastName;
            MiddleName = middleName;

            Status = UserStatus.PendingActivation;
            EmailVerified = false;
            PhoneVerified = false;
            CreatedAt = DateTimeOffset.UtcNow;
        }

        public string Email { get; private set; } = null!;

        public string? Phone { get; private set; }

        public string FirstName { get; private set; } = null!;

        public string LastName { get; private set; } = null!;

        public string? MiddleName { get; private set; }

        public UserStatus Status { get; private set; }

        public bool EmailVerified { get; private set; }

        public bool PhoneVerified { get; private set; }

        public DateTimeOffset? LastLoginAt { get; private set; }

        public bool IsDeleted { get; private set; }

        public DateTimeOffset? DeletedAt { get; private set; }

        public Guid? DeletedBy { get; private set; }

        public UserProfile? Profile { get; private set; }

        private readonly List<Address> _addresses = [];

        public IReadOnlyCollection<Address> Addresses => _addresses.AsReadOnly();

        public static User Create(
            Guid id,
            string email,
            string? phone,
            string firstName,
            string lastName,
            string? middleName = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(email);
            ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
            ArgumentException.ThrowIfNullOrWhiteSpace(lastName);

            return new User(
                id,
                NormalizeEmail(email),
                NormalizePhone(phone),
                firstName.Trim(),
                lastName.Trim(),
                NormalizeOptionalText(middleName));
        }

        public void UpdateProfile(
            string firstName,
            string lastName,
            string? middleName,
            string? phone)
        {
            EnsureCanBeModified();

            ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
            ArgumentException.ThrowIfNullOrWhiteSpace(lastName);

            FirstName = firstName.Trim();
            LastName = lastName.Trim();
            MiddleName = NormalizeOptionalText(middleName);

            var normalizedPhone = NormalizePhone(phone);

            if (!string.Equals(Phone, normalizedPhone, StringComparison.Ordinal))
            {
                Phone = normalizedPhone;
                PhoneVerified = false;
            }

            UpdatedAt = DateTimeOffset.UtcNow;
        }


        public Address AddAddress(
            string country,
            string? region,
            string city,
            string street,
            string building,
            string? apartment,
            string? postalCode,
            string recipientName,
            string recipientPhone,
            bool setAsDefault = false)
        {
            EnsureCanBeModified();

            var shouldBeDefault =
                setAsDefault || _addresses.Count == 0;

            if (shouldBeDefault)
            {
                foreach (var existingAddress in _addresses)
                {
                    existingAddress.RemoveDefault();
                }
            }

            var address = Address.Create(
                Id,
                country,
                region,
                city,
                street,
                building,
                apartment,
                postalCode,
                recipientName,
                recipientPhone,
                shouldBeDefault);

            _addresses.Add(address);

            UpdatedAt = DateTimeOffset.UtcNow;

            return address;
        }

        public void SetDefaultAddress(Guid addressId)
        {
            EnsureCanBeModified();

            var address = _addresses.FirstOrDefault(x => x.Id == addressId);

            if (address is null)
            {
                throw new UserDomainException(
                    "Address was not found.");
            }

            foreach (var item in _addresses)
            {
                item.RemoveDefault();
            }

            address.SetAsDefault();

            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public void RemoveAddress(Guid addressId)
        {
            EnsureCanBeModified();

            var address = _addresses.FirstOrDefault(x => x.Id == addressId);

            if (address is null)
            {
                throw new UserDomainException(
                    "Address was not found.");
            }

            var wasDefault = address.DefaultAddress;

            _addresses.Remove(address);

            if (wasDefault && _addresses.Count > 0)
            {
                _addresses[0].SetAsDefault();
            }

            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public void ChangeEmail(string email)
        {
            EnsureCanBeModified();

            ArgumentException.ThrowIfNullOrWhiteSpace(email);

            var normalizedEmail = NormalizeEmail(email);

            if (string.Equals(
                    Email,
                    normalizedEmail,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Email = normalizedEmail;
            EmailVerified = false;
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public void VerifyEmail()
        {
            EnsureCanBeModified();

            EmailVerified = true;

            if (Status == UserStatus.PendingActivation)
            {
                Status = UserStatus.Active;
            }

            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public void VerifyPhone()
        {
            EnsureCanBeModified();

            if (string.IsNullOrWhiteSpace(Phone))
            {
                throw new UserDomainException(
                    "Cannot verify phone because user does not have a phone number.");
            }

            PhoneVerified = true;
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public void CreateProfile(
    DateOnly? birthDate = null,
    string? gender = null,
    string? avatarUrl = null,
    string language = "uk",
    bool marketingEmailsEnabled = false)
        {
            EnsureCanBeModified();

            if (Profile is not null)
            {
                throw new UserDomainException(
                    "User profile already exists.");
            }

            Profile = UserProfile.Create(
                Id,
                birthDate,
                gender,
                avatarUrl,
                language,
                marketingEmailsEnabled);

            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public void UpdateProfileDetails(
            DateOnly? birthDate,
            string? gender,
            string? avatarUrl,
            string language,
            bool marketingEmailsEnabled)
        {
            EnsureCanBeModified();

            if (Profile is null)
            {
                throw new UserDomainException(
                    "User profile does not exist.");
            }

            Profile.Update(
                birthDate,
                gender,
                avatarUrl,
                language,
                marketingEmailsEnabled);

            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public void RegisterLogin()
        {
            EnsureCanLogin();

            LastLoginAt = DateTimeOffset.UtcNow;
        }

        public void Block()
        {
            if (Status == UserStatus.Deleted)
            {
                throw new UserDomainException(
                    "Deleted user cannot be blocked.");
            }

            if (Status == UserStatus.Blocked)
            {
                return;
            }

            Status = UserStatus.Blocked;
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public void Unblock()
        {
            if (Status != UserStatus.Blocked)
            {
                throw new UserDomainException(
                    "Only blocked user can be unblocked.");
            }

            Status = EmailVerified
                ? UserStatus.Active
                : UserStatus.PendingActivation;

            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public void Delete(Guid? deletedBy)
        {
            if (Status == UserStatus.Deleted)
            {
                return;
            }

            Status = UserStatus.Deleted;
            IsDeleted = true;
            DeletedAt = DateTimeOffset.UtcNow;
            DeletedBy = deletedBy;
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        private void EnsureCanBeModified()
        {
            if (Status == UserStatus.Deleted)
            {
                throw new UserDomainException(
                    "Deleted user cannot be modified.");
            }
        }

        private void EnsureCanLogin()
        {
            if (Status == UserStatus.Blocked)
            {
                throw new UserDomainException(
                    "Blocked user cannot login.");
            }

            if (Status == UserStatus.Deleted)
            {
                throw new UserDomainException(
                    "Deleted user cannot login.");
            }
        }

        private static string NormalizeEmail(string email)
        {
            return email.Trim().ToLowerInvariant();
        }

        private static string? NormalizePhone(string? phone)
        {
            return string.IsNullOrWhiteSpace(phone)
                ? null
                : phone.Trim();
        }

        private static string? NormalizeOptionalText(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
    }
}