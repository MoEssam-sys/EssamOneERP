using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using One.Domain.Entities.Identity;
using One.Infrastructure.Interfaces;

namespace One.Application.Repos.Identity
{
    public class AuthRepository : IAuthRepository
    {
        private readonly UserManager<UserData> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly One.Infrastructure.DbContext.UsersDbContext.UsersDbContext _context;

        public AuthRepository(UserManager<UserData> userManager, RoleManager<IdentityRole> roleManager, One.Infrastructure.DbContext.UsersDbContext.UsersDbContext context)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
        }

        public async Task<UserData?> FindByEmailAsync(string email)
            => await _userManager.FindByEmailAsync(email);

        public async Task<UserData?> FindByUsernameAsync(string username)
            => await _userManager.FindByNameAsync(username);

        public async Task<IdentityResult> CreateUserAsync(UserData user, string password)
            => await _userManager.CreateAsync(user, password);

        public async Task<bool> CheckPasswordAsync(UserData user, string password)
            => await _userManager.CheckPasswordAsync(user, password);

        public async Task<IList<string>> GetRolesAsync(UserData user)
            => await _userManager.GetRolesAsync(user);

        public async Task<IdentityResult> AddToRoleAsync(UserData user, string role)
            => await _userManager.AddToRoleAsync(user, role);

        public async Task<bool> RoleExistsAsync(string role)
            => await _roleManager.RoleExistsAsync(role);

        public async Task<UserData> GetUserByIdAsync(string userId)
            => await _userManager.FindByIdAsync(userId) ?? new();

        // Company
        public async Task<Company?> FindCompanyByCodeAsync(string code)
        {
            return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(_context.Companies, c => c.Code == code);
        }

        public async Task<bool> IsCompanyEmptyAsync(int companyId)
        {
            // Use EF Core AnyAsync
            return !await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(_context.Users, u => u.CompanyId == companyId);
        }

        public async Task<Company> CreateCompanyAsync(string name, string code)
        {
            // Simple create for Debug/Onboarding
            var company = new Company { Name = name, Code = code, IsActive = true, CreatedOn = DateTime.UtcNow };
            _context.Companies.Add(company);
            await _context.SaveChangesAsync();
            return company;
        }

        public async Task<IList<Company>> GetAllCompaniesAsync()
        {
            return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(_context.Companies);
        }

        public async Task<IList<One.Domain.DTOs.CompanySummaryDto>> GetCompanySummariesAsync()
        {
            // Count users and admins (CompanyAdmin role)
            // Note: Counting roles via Navigation property is slightly complex if UserRoles are not directly navigable easily or if string based.
            // Simplified approach: Count all users for IsUsersCount.
            // For Admins, we need to join with UserRoles.
            // Let's do a client-side evaluation if dataset is small, OR a better LINQ query.
            // Since UserData has no "Roles" navigation property by default in IdentityUser unless extended...
            // We will do a separate query or assume small dataset for now.
            // A much better way is to do it in memory or raw SQL if performance needed.
            // Given "Propose changes for performance" in instructions, let's try a decent LINQ projection.

            // Since we need to know WHO is admin, and that info is in IdentityUserRole which is a join table.

            var companies = await _context.Companies
                .Include(c => c.Users)
                .ToListAsync();

            var result = new List<One.Domain.DTOs.CompanySummaryDto>();

            // Get all userIds that are CompanyAdmins to filter efficiently?
            // Better: Get all users in role "CompanyAdmin" first.
            var companyAdmins = await _userManager.GetUsersInRoleAsync("CompanyAdmin");
            var adminIds = companyAdmins.Select(u => u.Id).ToHashSet();

            foreach (var c in companies)
            {
                result.Add(new One.Domain.DTOs.CompanySummaryDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Code = c.Code,
                    IsActive = c.IsActive,
                    UsersCount = c.Users.Count,
                    AdminsCount = c.Users.Count(u => adminIds.Contains(u.Id)),
                    CreatedOn = c.CreatedOn
                });
            }

            return result;
        }


        public async Task<Company?> GetCompanyByIdAsync(int id)
        {
            return await _context.Companies
                .Include(c => c.Users)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task UpdateCompanyAsync(Company company)
        {
            _context.Companies.Update(company);
            await _context.SaveChangesAsync();
        }

        public async Task<string> GenerateOtpAsync(UserData user)
        {
            var code = new Random().Next(100000, 999999).ToString();
            // Hash or store plain for now? Requirement says Hashed.
            // Simple hash for demo:
            // In real app, use PasswordHasher or SHA256.
            // For now, I'll allow plain text storage if hashing is too much boilerplate, BUT requirements say MUST BE HASHED.
            // Let's use a simple SHA256 helper.

            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(code);
            var hash = Convert.ToBase64String(sha.ComputeHash(bytes));

            var otp = new OtpChallenge
            {
                UserId = user.Id,
                OtpHash = hash,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
                Attempts = 0
            };

            _context.OtpChallenges.Add(otp);
            await _context.SaveChangesAsync();

            return code; // Return plain code to send via email
        }

        public async Task<bool> ValidateOtpAsync(UserData user, string otp)
        {
            // Find valid OTPs for user
            var otps = await _context.OtpChallenges
                .Where(o => o.UserId == user.Id && o.UsedAtUtc == null && o.ExpiresAtUtc > DateTime.UtcNow)
                .OrderByDescending(o => o.CreatedAtUtc)
                .ToListAsync();

            if (!otps.Any()) return false;

            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(otp);
            var hash = Convert.ToBase64String(sha.ComputeHash(bytes));

            foreach (var challenge in otps)
            {
                if (challenge.Attempts >= 5) continue; // Lockout logic could be stricter

                if (challenge.OtpHash == hash)
                {
                    challenge.UsedAtUtc = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    return true;
                }
                else
                {
                    challenge.Attempts++;
                }
            }
            // Save attempt increments
            await _context.SaveChangesAsync();
            return false;
        }

        public async Task<IdentityResult> AddToRoleAsync(UserData user, string role, int? companyId = null)
        {
            return await _userManager.AddToRoleAsync(user, role);
        }

        // Management
        public async Task<bool> ToggleUserStatusAsync(string userId, bool isActive)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return false;

            user.IsActive = isActive;
            var result = await _userManager.UpdateAsync(user);
            return result.Succeeded;
        }

        public async Task<bool> ToggleCompanyStatusAsync(int companyId, bool isActive)
        {
            var company = await _context.Companies.FindAsync(companyId);
            if (company == null) return false;

            company.IsActive = isActive;
            _context.Companies.Update(company);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IList<UserData>> GetUsersByCompanyAsync(int companyId)
        {
            return await _userManager.Users
                .Where(u => u.CompanyId == companyId)
                .ToListAsync();
        }

        public async Task<IList<UserData>> GetAllUsersAsync()
        {
             return await _userManager.Users
                .Include(u => u.Company) // Include Company for UI
                .ToListAsync();
        }
    }
}