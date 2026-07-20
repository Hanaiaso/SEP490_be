using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;

namespace VietTien.API.Repositories.Implementations
{
    public class UserRepository : IUserRepository
    {
        private readonly ApplicationDbContext _context;

        public UserRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<User?> GetByIdAsync(Guid id)
            => await _context.Users.FirstOrDefaultAsync(u => u.Id == id);

        public async Task<User?> GetByEmailAsync(string email)
        {
            var normalizedEmail = email.ToLower().Trim();
            return await _context.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        }

        public async Task<User?> GetByGoogleIdAsync(string googleId)
        {
            var trimmed = googleId.Trim();
            return await _context.Users.FirstOrDefaultAsync(u => u.GoogleId == trimmed);
        }

        public async Task<User?> GetByRefreshTokenAsync(string refreshToken)
            => await _context.Users.FirstOrDefaultAsync(u => u.RefreshToken == refreshToken);

        public async Task<User?> GetByResetTokenAsync(string resetToken)
            => await _context.Users.FirstOrDefaultAsync(u => u.PasswordResetToken == resetToken);

        public async Task<bool> EmailExistsAsync(string email)
            => await _context.Users.AnyAsync(u => u.Email == email.ToLower().Trim());

        public async Task<bool> PhoneExistsAsync(string phone)
            => await _context.Users.AnyAsync(u => u.PhoneNumber == phone.Trim());

        public async Task AddAsync(User user)
        {
            user.Email = user.Email.ToLower().Trim();
            await _context.Users.AddAsync(user);
        }

        public void Update(User user)
            => _context.Users.Update(user);

        public async Task<IEnumerable<User>> GetAllAsync()
            => await _context.Users.ToListAsync();

        public async Task<CustomerProfile?> GetCustomerProfileByUserIdAsync(Guid userId)
        {
            return await _context.CustomerProfiles
                                 .Include(cp => cp.User)
                                 .FirstOrDefaultAsync(cp => cp.UserId == userId);
        }

        public async Task AddCustomerProfileAsync(CustomerProfile profile)
        {
            await _context.CustomerProfiles.AddAsync(profile);
        }

        public async Task<CustomerProfile?> GetCustomerProfileByPhoneAsync(string phone)
        {
            var trimmed = phone.Trim();
            var user = await _context.Users
                .Include(u => u.CustomerProfile)
                .FirstOrDefaultAsync(u => u.PhoneNumber == trimmed);
            return user?.CustomerProfile;
        }
    }
}
