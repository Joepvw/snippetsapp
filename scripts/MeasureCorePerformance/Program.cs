using System.Diagnostics;
using System.Text.Json;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Storage;
using SnippetLauncher.Core.Search;
var root = Path.Combine(Path.GetTempPath(), "snippet-perf-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var rows = new List<object>();
try {
foreach (var count in new[]{0,100,1000,5000}) {
var dir=Path.Combine(root,count.ToString()); Directory.CreateDirectory(dir);
for(var i=0;i<count;i++) File.WriteAllText(Path.Combine(dir,$"s-{i}.md"),$"---\ntitle: Antwoord klant {i}\ntags: [klant, mail]\n---\nBeste klant, bedankt voor je bericht over afspraak {i}.");
for(var repeat=0;repeat<3;repeat++) {
using var usage=new UsageStore(Path.Combine(root,"usage.json"),new Clock());
using var repo=new SnippetRepository(dir,usage,new Clock());
var notifications=0; var rebuildMs=0d;
void Rebuild(){var sw=Stopwatch.StartNew();var list=repo.GetAll().OrderBy(x=>x.Title).ToArray();rebuildMs+=sw.Elapsed.TotalMilliseconds;notifications++;}
var bulk=repo.GetType().GetEvent("LibraryChanged");
if(bulk is not null) bulk.AddEventHandler(repo,new EventHandler((_,_)=>Rebuild()));
else repo.SnippetChanged+=(_,_)=>Rebuild();
var timer=Stopwatch.StartNew();await repo.LoadAllAsync();var load=timer.Elapsed.TotalMilliseconds;
var search=new SearchService(repo,new Clock()); search.Query("klant");
var times=new List<double>();for(var j=0;j<30;j++){timer.Restart();search.Query(j%2==0?"klant":"afspraak");times.Add(timer.Elapsed.TotalMilliseconds);}
times.Sort();rows.Add(new{count,repeat,loadMs=load,listRefreshes=notifications,listRefreshMs=rebuildMs,queryP50Ms=times[15],queryP95Ms=times[28]});
}
}
Console.WriteLine(JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
}finally{Directory.Delete(root,true);}
sealed class Clock:IClock{public DateTimeOffset UtcNow=>DateTimeOffset.UtcNow;}
