﻿using Excess.Compiler.Roslyn;
using Excess.Compiler.Tests.TestRuntime;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Excess.Compiler.Tests
{
    [TestClass]
    public class ConcurrentDemos
    {
        [TestMethod]
        public void DiningPhilosophers()
        {
            var errors = null as IEnumerable<Diagnostic>;
            var node = ConcurrentMock
                .Build(@"
                concurrent class philosopher 
                {
                    static int Meals = 10;

                    void main(string name, chopstick left, chopstick right) 
	                {
                        _name  = name;
	                    _left  = left;
	                    _right = right;
                               
                        for(int i = 0; i < Meals; i++)
                        {
	                        await think();
                        }    
	                }
	
	                void think()
	                {
                        console.write(_name + "" is thinking"");
                        seconds(rand(1.0, 2.0))
                            >> hungry();
	                }

	                void hungry()
	                {
                        console.write(_name + "" is hungry"");
	                    (_left.acquire(this) & _right.acquire(this)) 
                            >> eat();
	                }
	
	                void eat()
	                {
                        console.write(_name + "" is eating"");
                        await seconds(rand(1.0, 2.0));

                        _left.release(this); 
                        _right.release(this);
	                }
	                
                    private string _name;
                    private chopstick _left;
	                private chopstick _right;
                }

                concurrent class chopstick
                {
	                public void acquire(object owner)
                    {
                        if (_owner != null)
                        {
                            await release;
                        }
                        
                        _owner = owner;
                    }
	
	                public void release(object owner)
                    {
                        if (_owner != owner)
                            throw new InvalidOperationException();

                        _owner = null;
                    }

                    private object _owner;
                }", out errors, threads: 1);

            //must not have compilation errors
            Assert.IsNull(errors);

            var names = new[]
            {
                "Kant",
                "Archimedes",
                "Nietzche",
                "Plato",
                "Spinoza",
            };

            var chopsticks = names.Select(n => node
                .Spawn("chopstick"))
                .ToArray();

            var phCount = names.Length;
            for (int i = 0; i < phCount; i++)
            {
                var left = chopsticks[i];
                var right = i == phCount - 1 ? chopsticks[0] : chopsticks[i + 1];

                node.Spawn("philosopher", names[i], left, right);
            }

            Thread.Sleep(45000);
            node.Stop();
            node.WaitForCompletion();

            var items = console
                .items()
                .GroupBy(item =>
                {
                    var key = 0;
                    foreach (var name in names)
                    {
                        if (item.Contains(name))
                            return key;

                        key++;
                    }

                    Assert.IsTrue(false);
                    return -1;
                });
        }


        [TestMethod]
        public void Barbers()
        {
            var errors = null as IEnumerable<Diagnostic>;
            var node = ConcurrentMock
                .Build(@"
                concurrent class barbershop
                {
                    barber[] _barbers;
                    bool[] _busy;
                    
                    public barbershop(barber barber1, barber barber2)
                    {
                        _barbers = new [] {barber1, barber2}; 
                        _busy    = new [] {false, false}; 
                    }

                    public void visit(int client)
                    {
                        console.write($""Client: {client}, Barber1 {barber_status(0)},  Barber2: {barber_status(1)}"");
                        if (_busy[0] && _busy[1])
                            await visit.enqueue();

                        for(int i = 0; i < 2; i++)
                        {
                            if (!_busy[i]) 
                            {
                                await shave_client(client, i);
                                break;
                            }
                        }

                        visit.dequeue();
                    }

                    private void shave_client(int client, int which)
                    {
                        var barber = _barbers[which];
                        double tip = rand(5, 10);
                        
                        _busy[which] = true;

                        barber.shave(client)
                            >> barber.tip(client, tip);

                        _busy[which] = false;
                    }

                    private string barber_status(int which)
                    {
                        return _busy[which]
                            ? ""working""
                            : ""available"";
                    }
                }

                concurrent class barber
                {
                    int _index;
                    void main(int index)
                    {
                        _index = index;

                        while(true)
                            shave >> tip;
                    }

                    public void shave(int client)
                    {
                        await seconds(rand(1, 2));
                    }

                    double _tip = 0;
                    public void tip(int client, double amount)
                    {
                        _tip += amount;
                        console.write($""Barber {_index}: {client} tipped {amount:C2}, for a total of {_tip:C2}"");
                    }
                }", out errors, threads: 1);

            //must not have compilation errors
            Assert.IsNull(errors);

            var barber1 = node.Spawn("barber", 0);
            var barber2 = node.Spawn("barber", 1);
            var shop = node.Spawn("barbershop", barber1, barber2);

            var rand = new Random();
            var clients = 30;
            for (int i = 1; i <= clients; i++)
            {
                Thread.Sleep((int)(3000 * rand.NextDouble()));
                ConcurrentMock
                    .SendAsync(shop, "visit", i);
            }

            Thread.Sleep(3000); //wait for last one
            node.Stop();
            node.WaitForCompletion();

            var output = console.items();
            Assert.AreEqual(output.Length, 60);
        }

        [TestMethod]
        public void DebugPrint()
        {
            RoslynCompiler compiler = new RoslynCompiler();
            Extensions.Concurrent.Extension.Apply(compiler);

            SyntaxTree tree = null;
            string text = null;

            tree = compiler.ApplySemanticalPass(@"
                concurrent class ring_item
                {
                    int _idx;
                    public ring_item(int idx)
                    {
                        _idx = idx;
                    }
                    
                    public ring_item Next {get; set;}

                    static int ITERATIONS = 50*1000*1000;
                    public void token(int value)
                    {
                        console.write(value);
                        if (value >= ITERATIONS)
                        {
                            console.write(_idx);
                            Node.Stop();
                        }
                        else
                            Next.token(value + 1);
                    }                    
                }", out text);

            Assert.IsNotNull(text);
        }

    }
}
