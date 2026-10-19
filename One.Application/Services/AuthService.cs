using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using One.Domain.DTOs;
using One.Domain.DTOs.Identity;
using One.Domain.Entities.Identity;
using One.Infrastructure.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace One.Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly IAuthRepository _authRepository;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly UserManager<UserData> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AuthService(
            UserManager<UserData> userManager,
            RoleManager<IdentityRole> roleManager,
            IConfiguration configuration,
            IAuthRepository authRepository,
            IEmailService emailService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _configuration = configuration;
            _authRepository = authRepository;
            _emailService = emailService;
        }

        public async Task<Response> RegisterAsync(RegisterUserDto registerUserDto, string role, CancellationToken cancellationToken = default)
        {
            // Username uniqueness check (required for all users)
            var existingUserName = await _authRepository.FindByUsernameAsync(registerUserDto.UserName?.Trim() ?? "");
            if (existingUserName != null)
                return new Response { StatusCode = StatusCodes.Status400BadRequest, Message = "Username already exists", Messages = new List<string> { "Username already exists" } };

            if (!await _authRepository.RoleExistsAsync(role))
                return new Response { StatusCode = StatusCodes.Status400BadRequest, Message = "Role doesn't exist", Messages = new List<string> { "Role doesn't exist" } };

            var user = new UserData
            {
                UserName = registerUserDto.UserName?.Trim(),
                Email = registerUserDto.Email?.Trim(),
                FirstName = registerUserDto.FirstName,
                LastName = registerUserDto.LastName,
                Age = registerUserDto.Age,
                PhoneNumber = registerUserDto.PhoneNumber,
                SecurityStamp = Guid.NewGuid().ToString(),
                CompanyId = registerUserDto.CompanyId
            };

            // Multi-Tenant Checks
            if (registerUserDto.CompanyId.HasValue)
            {
                if (role == "CompanyAdmin")
                {
                    var isEmpty = await _authRepository.IsCompanyEmptyAsync(registerUserDto.CompanyId.Value);
                    if (!isEmpty)
                        return new Response { StatusCode = StatusCodes.Status400BadRequest, Message = "Company already has an Admin/Users.", Messages = new List<string> { "Company already has an Admin/Users. Cannot register another Admin." } };
                }
            }

            var result = await _authRepository.CreateUserAsync(user, registerUserDto.Password);
            if (!result.Succeeded)
                return new Response
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Message = "Registration failed",
                    Messages = result.Errors.Select(e => e.Description).ToList()
                };

            var roleresult = await _authRepository.AddToRoleAsync(user, role);
            if (!roleresult.Succeeded)
                return new Response
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Message = "Failed to assign role",
                    Messages = roleresult.Errors.Select(e => e.Description).ToList()
                };

            return new Response { StatusCode = StatusCodes.Status200OK, Message = "User created successfully" };
        }

        public async Task<One.Domain.Base.Result<string>> LoginWithOtpAsync(string email, CancellationToken cancellationToken = default)
        {
            try
            {
                var user = await _authRepository.FindByEmailAsync(email);
                if (user == null)
                {
                    // Return success to prevent enumeration? Or fail?
                    // Requirement says "Check if that email belongs to a SuperAdmin. If yes: send OTP."
                    // Implications: if not, do nothing?
                    return One.Domain.Base.Result<string>.Fail(new List<string?> { "Invalid Request" });
                }

                var roles = await _authRepository.GetRolesAsync(user);
                if (!roles.Contains("SuperAdmin"))
                {
                    return One.Domain.Base.Result<string>.Fail(new List<string?> { "Invalid Request" });
                }

                var otp = await _authRepository.GenerateOtpAsync(user);

                // Send Email
                await _emailService.SendEmailAsync(email, "Your OTP Code", $"Your login code is: <b>{otp}</b>. It expires in 10 minutes.");

                return One.Domain.Base.Result<string>.Ok("OTP Sent Successfully");
            }
            catch (Exception ex)
            {
                return One.Domain.Base.Result<string>.Fail(new List<string?> { $"Error: {ex.Message}. (Check logs/SMTP config)" });
            }
        }

        public async Task<AuthResponseDto> VerifyOtpAsync(string email, string otp, CancellationToken cancellationToken = default)
        {
            var user = await _authRepository.FindByEmailAsync(email);
            if (user == null)
                return new AuthResponseDto { StatusCode = StatusCodes.Status400BadRequest, Message = "Invalid Request" };

            var valid = await _authRepository.ValidateOtpAsync(user, otp);
            if (!valid)
                return new AuthResponseDto { StatusCode = StatusCodes.Status400BadRequest, Message = "Invalid OTP or Expired" };

            // Generate Token
            var roles = await _authRepository.GetRolesAsync(user);
            var claims = new List<Claim>
             {
                 new("firstName", user.FirstName ?? ""),
                 new("lastName", user.LastName ?? ""),
                 new("userId", user.Id),
                 new("companyId", user.CompanyId?.ToString() ?? ""),
                 new(ClaimTypes.NameIdentifier, user.Id),
                 new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
             };
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
            var token = GetToken(claims);

            return new AuthResponseDto
            {
                StatusCode = StatusCodes.Status200OK,
                Message = "Login Successful",
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                Expiration = token.ValidTo
            };
        }

        public async Task<One.Domain.Base.Result<CompanyValidationDto>> ValidateCompanyAsync(string code, CancellationToken cancellationToken = default)
        {
            var company = await _authRepository.FindCompanyByCodeAsync(code);
            if (company == null)
                return One.Domain.Base.Result<CompanyValidationDto>.Fail(new List<string?> { "Invalid Company Code" });

            if (!company.IsActive)
                return One.Domain.Base.Result<CompanyValidationDto>.Fail(new List<string?> { "Company is inactive" });

            var isEmpty = await _authRepository.IsCompanyEmptyAsync(company.Id);

            return One.Domain.Base.Result<CompanyValidationDto>.Ok(new CompanyValidationDto
            {
                CompanyId = company.Id,
                Name = company.Name,
                RequiresAdminRegistration = isEmpty
            });
        }

        public async Task<AuthResponseDto> LoginAsync(LoginDto loginDto, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(loginDto.Email) && string.IsNullOrWhiteSpace(loginDto.UserName))
                return new AuthResponseDto { StatusCode = StatusCodes.Status400BadRequest, Message = "Please enter a valid Email or UserName" };

            var user = new UserData();
            if (loginDto.Email is not null && loginDto.Email != "")
            {
                user = await _authRepository.FindByEmailAsync(loginDto.Email);
                if (user is null)
                    return new AuthResponseDto { StatusCode = StatusCodes.Status400BadRequest, Message = "Wrong Email" };
            }
            else if (loginDto.UserName is not null && loginDto.UserName != "")
            {
                user = await _authRepository.FindByUsernameAsync(loginDto.UserName);
                if (user is null)
                    return new AuthResponseDto { StatusCode = StatusCodes.Status400BadRequest, Message = "Wrong UserName" };
            }
            if (!await _authRepository.CheckPasswordAsync(user, loginDto.Password))
                return new AuthResponseDto { StatusCode = StatusCodes.Status400BadRequest, Message = "Wrong Password" };

            var roles = await _authRepository.GetRolesAsync(user);

            var claims = new List<Claim>
            {
                new("firstName", user.FirstName ?? ""),
                new("lastName", user.LastName ?? ""),
                new("userId", user.Id),
                new("companyId", user.CompanyId?.ToString() ?? ""), // Add CompanyId
                new(ClaimTypes.NameIdentifier, user.Id),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));  // [Authorize(Roles = "Admin")]
            var token = GetToken(claims);

            return new AuthResponseDto
            {
                StatusCode = StatusCodes.Status200OK,
                Message = "Login Successful",
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                Expiration = token.ValidTo
            };
        }

        public async Task<One.Domain.Base.Result<List<One.Domain.DTOs.CompanySummaryDto>>> GetCompanySummariesAsync(CancellationToken cancellationToken = default)
        {
            var data = await _authRepository.GetCompanySummariesAsync();
            return One.Domain.Base.Result<List<One.Domain.DTOs.CompanySummaryDto>>.Ok(data.ToList());
        }

        public async Task<One.Domain.Base.Result<One.Domain.DTOs.CompanyDetailsDto>> GetCompanyDetailsAsync(int id, CancellationToken cancellationToken = default)
        {
            var company = await _authRepository.GetCompanyByIdAsync(id);
            if (company == null) return One.Domain.Base.Result<One.Domain.DTOs.CompanyDetailsDto>.Fail(new List<string?> { "Company not found" });

            // Fetch roles for all users in this company
            // This is N+1 if not careful.
            // Optimized approach for now:
            // Iterate and fetch roles. (Acceptable for < 100 users per company for now).
            // A better approach would be to join AspNetUserRoles.

            var userDtos = new List<One.Domain.DTOs.UserDetailDto>();
            foreach (var user in company.Users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                userDtos.Add(new One.Domain.DTOs.UserDetailDto
                {
                    Id = user.Id,
                    UserName = user.UserName ?? "",
                    Email = user.Email ?? "",
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    PhoneNumber = user.PhoneNumber ?? "",
                    Roles = roles.ToList()
                });
            }

            var dto = new One.Domain.DTOs.CompanyDetailsDto
            {
                Id = company.Id,
                Name = company.Name,
                Code = company.Code,
                IsActive = company.IsActive,
                UsersCount = userDtos.Count, // Logic consistent
                AdminsCount = userDtos.Count(u => u.Roles.Contains("CompanyAdmin")),
                Users = userDtos
            };

            return One.Domain.Base.Result<One.Domain.DTOs.CompanyDetailsDto>.Ok(dto);
        }

        public async Task<UserInfoDto> GetCurrentUserAsync(ClaimsPrincipal userClaims, CancellationToken cancellationToken = default)
        {
            var userId = userClaims.Claims.FirstOrDefault(c => c.Type == "userId")?.Value;

            if (string.IsNullOrWhiteSpace(userId))
                return new();

            var user = await _authRepository.GetUserByIdAsync(userId);
            if (user == null)
                return new();

            return new UserInfoDto
            {
                UserName = user.UserName ?? "",
                Email = user.Email ?? "",
                FirstName = user.FirstName,
                LastName = user.LastName,
                Age = user.Age,
                PhoneNumber = user.PhoneNumber ?? "",
            };
        }


        public async Task<One.Domain.Base.Result<List<UserManagementDto>>> GetAllUsersAsync(CancellationToken cancellationToken = default)
        {
            var users = await _authRepository.GetAllUsersAsync();
            var dtos = new List<UserManagementDto>();
            foreach (var user in users)
            {
                var roles = await _authRepository.GetRolesAsync(user);
                dtos.Add(new UserManagementDto
                {
                    Id = user.Id,
                    UserName = user.UserName ?? "",
                    Email = user.Email ?? "",
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    CompanyId = user.CompanyId,
                    CompanyName = user.Company?.Name ?? "N/A",
                    Roles = roles.ToList(),
                    IsActive = user.IsActive
                });
            }
            return One.Domain.Base.Result<List<UserManagementDto>>.Ok(dtos);
        }

        public async Task<One.Domain.Base.Result<List<UserManagementDto>>> GetUsersByCompanyAsync(int companyId, CancellationToken cancellationToken = default)
        {
            var users = await _authRepository.GetUsersByCompanyAsync(companyId);
            var dtos = new List<UserManagementDto>();
            foreach (var user in users)
            {
                var roles = await _authRepository.GetRolesAsync(user);
                dtos.Add(new UserManagementDto
                {
                    Id = user.Id,
                    UserName = user.UserName ?? "",
                    Email = user.Email ?? "",
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    CompanyId = user.CompanyId,
                    CompanyName = user.Company?.Name, // Assuming included or not strictly needed if we know the company
                    Roles = roles.ToList(),
                    IsActive = user.IsActive
                });
            }
            return One.Domain.Base.Result<List<UserManagementDto>>.Ok(dtos);
        }

        public async Task<One.Domain.Base.Result<bool>> ToggleUserStatusAsync(string userId, bool isActive, CancellationToken cancellationToken = default)
        {
             var success = await _authRepository.ToggleUserStatusAsync(userId, isActive);
             if (!success) return One.Domain.Base.Result<bool>.Fail(new List<string?> { "User not found or update failed" });
             return One.Domain.Base.Result<bool>.Ok(true);
        }

        public async Task<One.Domain.Base.Result<bool>> ToggleCompanyStatusAsync(int companyId, bool isActive, CancellationToken cancellationToken = default)
        {
             var success = await _authRepository.ToggleCompanyStatusAsync(companyId, isActive);
             if (!success) return One.Domain.Base.Result<bool>.Fail(new List<string?> { "Company not found or update failed" });
             return One.Domain.Base.Result<bool>.Ok(true);
        }

        #region Helper Methods

        private JwtSecurityToken GetToken(List<Claim> claims)
        {
            var secret = _configuration["JWT:Secret"];
            if (string.IsNullOrEmpty(secret))
                throw new InvalidOperationException("JWT:Secret configuration value is missing or empty.");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            return new JwtSecurityToken(
                issuer: _configuration["JWT:ValidIssuer"],
                audience: _configuration["JWT:ValidAudience"],
                expires: DateTime.UtcNow.AddHours(5),
                claims: claims,
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        }

        #endregion Helper Methods
    }
}
