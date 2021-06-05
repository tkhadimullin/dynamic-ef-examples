using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

namespace DynamicGroupBy
{
    public class Entity
    {
        public int Id { get; set; }
    }
    class Company : Entity
    {
        public string CompanyName { get; set; }
    }

    class Team : Entity
    {
        public string TeamName { get; set; }
        public Company Company { get; set; }
    }

    class Employee : Entity
    {
        public string EmployeeName { get; set; }
        public Team Team { get; set; }
    }

    // --- DbContext
    class MyDbContext : DbContext
    {
        public DbSet<Company> Companies { get; set; }
        public DbSet<Team> Teams { get; set; }
        public DbSet<Employee> Employees { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseMySql("Server=localhost;Database=...;Uid=...;Pwd=...;");
            base.OnConfiguring(optionsBuilder);
        }
    }


    class DynamicFilters<T> where T : Entity
    {
        private readonly DbContext _context;

        public DynamicFilters(DbContext context)
        {
            _context = context;
        }

        public IQueryable<Tuple<object, int>> Filter(IEnumerable<string> queryableFilters = null)
        {
            IQueryable<T> mainQuery = _context.Set<T>().AsQueryable().AsNoTracking();           

            return mainQuery.BuildExpression(_context, queryableFilters.ToList());
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var context = new MyDbContext();
            context.Database.EnsureCreated();

            var someTableData = new DynamicFilters<Employee>(context).Filter(new List<string> { "Employees.Teams.Companies.Id" });

            var stub = context.Teams.Include(t => t.Company);
            var sql = stub.GroupBy(k => new { k.Company.Id })
                .Select(g => new { g.Key, Count = g.Count() })
                .AsQueryable().AsNoTracking()
                .ToSql();
            
            //Console.WriteLine(sql1);
            /*
             SELECT `t`.`Id` AS `Key`, COUNT(*) AS `Count`
            FROM `Teams` AS `t`
            GROUP BY `t`.`Id`

            SELECT `c`.`Id` AS `Key`, COUNT(*) AS `Count`
            FROM `Teams` AS `t`
            LEFT JOIN `Companies` AS `c` ON `t`.`CompanyId` = `c`.`Id`
            GROUP BY `c`.`Id`
             */
            Console.ReadKey();
        }
    }
}
