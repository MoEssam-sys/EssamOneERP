using Microsoft.AspNetCore.Identity;
using One.Domain.Entities.Identity;
using System.Security.Claims;

namespace One.Infrastructure.Interfaces
{
    public interface IAuthRepository
    {
        Task<UserData?> FindByEmailAsync(string email);

        Task<UserData?> FindByUsernameAsync(string username);

        Task<IdentityResult> CreateUserAsync(UserData user, string password);

        Task<bool> CheckPasswordAsync(UserData user, string password);

        Task<IList<string>> GetRolesAsync(UserData user);

        Task<IdentityResult> AddToRoleAsync(UserData user, string role);

        Task<bool> RoleExistsAsync(string role);

        Task<UserData> GetUserByIdAsync(string userId);

        // Company
        Task<Company?> FindCompanyByCodeAsync(string code);
        Task<bool> IsCompanyEmptyAsync(int companyId);
        Task<Company> CreateCompanyAsync(string name, string code);
        Task<IList<Company>> GetAllCompaniesAsync();
        Task<IList<One.Domain.DTOs.CompanySummaryDto>> GetCompanySummariesAsync();
        Task<Company?> GetCompanyByIdAsync(int id);

        Task UpdateCompanyAsync(Company company);

        // OTP & Roles
        Task<string> GenerateOtpAsync(UserData user);
        Task<bool> ValidateOtpAsync(UserData user, string otp);
        Task<IdentityResult> AddToRoleAsync(UserData user, string role, int? companyId = null);

        // Management
        Task<bool> ToggleUserStatusAsync(string userId, bool isActive);
        Task<bool> ToggleCompanyStatusAsync(int companyId, bool isActive);
        Task<IList<UserData>> GetUsersByCompanyAsync(int companyId);
        Task<IList<UserData>> GetAllUsersAsync();
    }
}
