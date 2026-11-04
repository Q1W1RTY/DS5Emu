using System;
using System.Threading.Tasks;

namespace DS5Link;

internal static class ProfileApplication {
 internal static async Task ApplyAsync(GameProfile target,GameProfile previous,Func<InputProfile,Task> input,Func<EffectOptions,Task> effects){
  try{await input(target.Input);await effects(target.Effects);}
  catch(Exception original){
   Exception? inputFailure=null,effectFailure=null;
   // Attempt both restores even if one channel is unavailable.
   try{await input(previous.Input);}catch(Exception error){inputFailure=error;}
   try{await effects(previous.Effects);}catch(Exception error){effectFailure=error;}
   if(inputFailure!=null || effectFailure!=null)throw new AggregateException("Не удалось полностью восстановить предыдущий профиль.",original,inputFailure??effectFailure!);
   throw;
  }
 }
}
