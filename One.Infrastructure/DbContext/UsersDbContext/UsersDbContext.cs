using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using One.Domain.Entities.Identity;
using One.Infrastructure.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace One.Infrastructure.DbContext.UsersDbContext
{
    public class UsersDbContext : IdentityDbContext<UserData>
    {
        public UsersDbContext(DbContextOptions<UsersDbContext> options) : base(options)
        {
        }

        public DbSet<Company> Companies { get; set; }
        public DbSet<OtpChallenge> OtpChallenges { get; set; }
        public DbSet<AppLog> AppLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Company>()
                .HasIndex(c => c.Code)
                .IsUnique();

            builder.Entity<Company>()
                .Property(c => c.IsActive)
                .HasDefaultValue(true)
                .ValueGeneratedOnAdd();

            builder.Entity<UserData>()
                .HasOne(u => u.Company)
                .WithMany(c => c.Users)
                .HasForeignKey(u => u.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // =========================================================================
            // Seeding Roles (Deterministic IDs & Stamps)
            // =========================================================================
            // Use static constants to prevent PendingModelChangesWarning
            builder.Entity<IdentityRole>().HasData(
                new IdentityRole { Id = SeedConstants.SuperAdminRoleId, Name = "SuperAdmin", NormalizedName = "SUPERADMIN", ConcurrencyStamp = SeedConstants.SuperAdminRoleConcurrencyStamp },
                new IdentityRole { Id = SeedConstants.CompanyAdminRoleId, Name = "CompanyAdmin", NormalizedName = "COMPANYADMIN", ConcurrencyStamp = SeedConstants.CompanyAdminRoleConcurrencyStamp },
                new IdentityRole { Id = SeedConstants.UserRoleId, Name = "User", NormalizedName = "USER", ConcurrencyStamp = SeedConstants.UserRoleConcurrencyStamp },
                new IdentityRole { Id = SeedConstants.TesterRoleId, Name = "Tester", NormalizedName = "TESTER", ConcurrencyStamp = SeedConstants.TesterRoleConcurrencyStamp }
            );

            // =========================================================================
            // Seeding Super Admin
            // =========================================================================
            var superAdminUser = new UserData
            {
                Id = SeedConstants.SuperAdminUserId,
                UserName = "loai.asp97@gmail.com",
                NormalizedUserName = "LOAI.ASP97@GMAIL.COM",
                Email = "loai.asp97@gmail.com",
                NormalizedEmail = "LOAI.ASP97@GMAIL.COM",
                EmailConfirmed = true,
                FirstName = "Super",
                LastName = "Admin",
                CompanyId = null, // Global
                SecurityStamp = SeedConstants.SuperAdminSecurityStamp,
                ConcurrencyStamp = SeedConstants.SuperAdminConcurrencyStamp
            };

            builder.Entity<UserData>().HasData(superAdminUser);

            // Assign SuperAdmin Role
            builder.Entity<IdentityUserRole<string>>().HasData(new IdentityUserRole<string>
            {
                RoleId = SeedConstants.SuperAdminRoleId,
                UserId = SeedConstants.SuperAdminUserId
            });
        }
    }
}