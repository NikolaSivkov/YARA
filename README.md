# YARA
Yet Another Roslyn Analyzer


##Fixes
hr
This analyzer currently provides analysis and code fix for the following cases :

#####YARA001-EFAsync
Methods that have been marked `async` will enable diagnostic analyzer where it will point out that extension methods 
to EntityFramework that can be made async but are currently not and could cause performance degradations
Example : 
```csharp
myDbContext.MyTable.ToList();
//Will be transfromed to 
await myDbContext.MyTable.ToListAsync();
```
```csharp
myDbContext.MyTable.First(x=>x.foo =="foo");
//Will be transfromed to 
await myDbContext.MyTable.FirstAsync(x=>x.foo =="foo");
```

```csharp
myDbContext.MyTable.Where(x=>x.foo =="foo").ToList();
//Will be transfromed to 
await myDbContext.MyTable.Where(x=>x.foo =="foo").ToListAsync();
```
