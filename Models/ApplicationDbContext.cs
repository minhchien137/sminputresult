using Microsoft.EntityFrameworkCore;

namespace SMInputProduction.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<SVN_Production_result_Viindoo> SVN_Production_result_Viindoo { get; set; }
        public DbSet<SVN_Target> SVN_Target { get; set; }
        public DbSet<SM_InputProductionResultHistory> SM_InputProductionResultHistory { get; set; }
        public DbSet<SVN_Defect_Record> SVN_Defect_Record { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SVN_Production_result_Viindoo>().HasNoKey()
                .ToTable("SVN_Production_result_Viindoo");

            modelBuilder.Entity<SVN_Target>().HasNoKey().ToTable("SVN_target");

            modelBuilder.Entity<SM_InputProductionResultHistory>()
                .ToTable("SM_InputProductionResultHistory");

            modelBuilder.Entity<SVN_Defect_Record>().HasNoKey().ToTable("SVN_Defect_Record");
        }

    }
}