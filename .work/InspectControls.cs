using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
class Program {
 static void Main(){
  var a=Assembly.LoadFrom(@"C:\Users\ADMIN\YukkuriMovieMaker_v4_Lite\YukkuriMovieMaker.Controls.dll");
  foreach(var n in new[]{"YukkuriMovieMaker.Controls.ToggleSlider","YukkuriMovieMaker.Controls.TextBoxSlider","YukkuriMovieMaker.Controls.AnimationSlider","YukkuriMovieMaker.Controls.ToggleSliderAttribute","YukkuriMovieMaker.Controls.TextBoxSliderAttribute","YukkuriMovieMaker.Controls.AnimationSliderAttribute"}){
   var t=a.GetType(n); Console.WriteLine("TYPE "+n+" => "+t); if(t==null)continue;
   Console.WriteLine("BASE "+t.BaseType);
   foreach(var c in t.GetConstructors(BindingFlags.Public|BindingFlags.Instance|BindingFlags.NonPublic)) Console.WriteLine("CTOR "+c);
   foreach(var p in t.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)) Console.WriteLine("PROP "+p);
  }
 }
}
