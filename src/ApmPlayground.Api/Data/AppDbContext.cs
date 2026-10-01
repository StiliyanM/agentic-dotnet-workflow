using Microsoft.EntityFrameworkCore;

namespace ApmPlayground.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
