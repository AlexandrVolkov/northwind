using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private static byte[] ProductionRecord()
    {
        var r = Enumerable.Repeat((byte)0xa5, 49).ToArray(); r[0]=2; r[3]=0; r[0x0e]=2; r[0x14]=0xc9; r[0x15]=0;
        r.AsSpan(0x22,5).Fill(0xff); return r;
    }
    private void OriginalProductionChecks()
    {
        using var doc=GameData.Read<JsonDocument>("res://tests/fixtures/production.json");
        Check(doc.RootElement.GetProperty("format_version").GetInt32()==1,"Production reference schema");
        static ResourceQuad Quad(JsonElement e) { var v=e.EnumerateArray().Select(x=>x.GetUInt32()).ToArray();return new(v[0],v[1],v[2],v[3]); }
        foreach (string name in new[] {"enqueue","start","tick","advance","refund","exit"})
        {
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256); int count=0;
            var group=doc.RootElement.GetProperty(name);
            foreach (var c in group.GetProperty("cases").EnumerateArray())
            {
                var r=ProductionRecord(); byte[] output;
                if (name=="start") { r[0x15]=c.GetByte(); OriginalProductionRules.StartPaid(r); output=r; }
                else if (name=="refund")
                {
                    var q=OriginalProductionRules.Refund(Quad(c[1]),Quad(c[0]),c[2].GetByte(),c[3].GetByte());output=new byte[17];
                    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(1),q.First);BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(5),q.Second);
                    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(9),q.Third);BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(13),q.Fourth);
                }
                else if (name=="exit")
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(4),c[0].GetUInt32());BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(8),c[1].GetUInt32());
                    var unit=new byte[171];unit[3]=c[2].GetByte();unit[4]=c[3].GetByte();unit[0x77]=c[5].GetByte();var building=new byte[171];
                    c[4].EnumerateArray().Select(v=>v.GetByte()).ToArray().CopyTo(building,0x53);
                    var exit=OriginalProductionRules.Exit(r,building,unit);output=new byte[9];
                    BinaryPrimitives.WriteUInt32LittleEndian(output,exit.X);BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4),exit.Y);output[8]=exit.Layer;
                }
                else if (name=="enqueue")
                {
                    var values=c.EnumerateArray().Select(x=>x.GetByte()).ToArray();values.AsSpan(0,5).CopyTo(r.AsSpan(0x22));
                    bool accepted=OriginalProductionRules.Enqueue(r,values[5]);output=new byte[50];output[0]=accepted?(byte)1:(byte)0;r.CopyTo(output,1);
                }
                else if (name=="advance")
                { c.EnumerateArray().Select(x=>x.GetByte()).ToArray().CopyTo(r,0x22);r[0x15]=1;OriginalProductionRules.Complete(r);output=r; }
                else
                {
                    r[0x22]=0;r[0x28]=c[2].GetByte();r[0x27]=c[3].GetByte();r[0x15]=1;
                    bool ready=OriginalProductionRules.Tick(r,c[0].GetByte(),c[1].GetByte(),c[4].GetByte());
                    output=new byte[50];output[0]=ready?(byte)1:(byte)0;r.CopyTo(output,1);
                }
                hash.AppendData(output);count++;
            }
            Check(count==group.GetProperty("case_count").GetInt32() && Convert.ToHexStringLower(hash.GetHashAndReset())==group.GetProperty("sha256").GetString(),
                $"Production {name} matches original x86, including unchanged object bytes");
        }
    }
    private async Task ProductionChecks()
    {
        OriginalProductionChecks();
        using (var battle=CreateSkirmish())
        {
            var stocks=battle.Resources; int actors=battle.Actors.Count;
            for (int i=0;i<5;i++) Check(battle.QueueWorker(),"Units can queue before enough free people exist");
            Check(!battle.QueueMilitia() && battle.RecruitmentCount==5 && battle.Resources==stocks && battle.Actors.Count==actors,"Five native queue slots reject a sixth order without spending or spawning");
            battle.Advance(2);
            Check(!battle.RecruitmentPaid && battle.Resources==stocks && battle.Actors.Count==actors,"An unpaid head waits without partial debits");
            Check(battle.CancelRecruitment() && battle.RecruitmentCount==0 && battle.Resources==stocks,"Clearing an unpaid queue does not grant a refund");
        }
        using (var battle=CreateFundedSkirmish())
        {
            int actors=battle.Actors.Count;var stocks=battle.Resources;
            Check(battle.QueueWorker() && battle.QueueMilitia() && battle.QueueWorker(),"Mixed orders append in FIFO order");
            battle.Advance(1.0/battle.Rules.TicksPerSecond);
            Check(battle.RecruitmentPaid && battle.Resources==new ResourceQuad(stocks.First-5,stocks.Second,stocks.Third-40,stocks.Fourth-1) && battle.Actors.Count==actors,
                "Only the head pays once before preparation; queued tails remain unpaid");
            battle.Advance(29.0/battle.Rules.TicksPerSecond);
            Check(battle.PlayerCastle.Record[0x27]==0,"Worker progress waits for its original 30-call interval");
            battle.Advance(1.0/battle.Rules.TicksPerSecond);
            Check(battle.PlayerCastle.Record[0x27]==1 && battle.Actors.Count==actors,"First due interval advances progress without producing a unit");
            battle.Paused=true;var raw=battle.PlayerCastle.Record.ToArray();battle.Advance(20);
            Check(raw.SequenceEqual(battle.PlayerCastle.Record) && !battle.CancelRecruitment() && !battle.QueueWorker(),"Pause freezes preparation and rejects queue mutations");battle.Paused=false;
            var exit=battle.RecruitExit(battle.WorkerTemplate);var cell=new Vector2I((int)(exit.X>>5),(int)(exit.Y>>5));
            var grid=battle.Occupancy.Cells.ToArray();int index=(exit.Layer*battle.Map.Height+cell.Y)*battle.Map.Width+cell.X;
            uint old=grid[index];grid[index]=battle.Hero.Id;battle.Occupancy.LoadCells(grid);
            var paid=battle.Resources;
            for (int i=0;i<240 && !battle.RecruitmentBlocked;i++) battle.Advance(1.0/battle.Rules.TicksPerSecond);
            Check(battle.RecruitmentBlocked && battle.PlayerCastle.Record[0x27]==5 && battle.Actors.Count==actors && battle.RecruitmentCount==3,
                "Ready production waits at the fixed occupied exit without advancing the queue");
            battle.Advance(1);
            Check(battle.Resources==paid && battle.Actors.Count==actors,"Blocked retries neither charge again nor create a displaced recruit");
            grid=battle.Occupancy.Cells.ToArray();grid[index]=old;battle.Occupancy.LoadCells(grid);
            var worker=WaitForRecruit(battle,0);
            Check(worker.Source.TemplateIndex==10 && worker.Position==new Vector2(exit.X,exit.Y) && battle.RecruitmentCount==2 && !battle.RecruitmentPaid,
                "Freeing the exact exit releases one worker and shifts the next order to unpaid phase");
            battle.Advance(1.0/battle.Rules.TicksPerSecond);
            Check(battle.RecruitmentPaid && battle.RecruitmentTemplate?.TemplateIndex==0 && battle.Resources.First==paid.First-10 && battle.Resources.Third==paid.Third-40,
                "Next simulation call pays the militia using its own DAT costs");
            for (int i=0;i<39;i++) battle.Advance(1.0/battle.Rules.TicksPerSecond);
            Check(battle.PlayerCastle.Record[0x27]==0,"Militia waits all 40 calls from its barracks WRK interval");
            battle.Advance(1.0/battle.Rules.TicksPerSecond);
            Check(battle.PlayerCastle.Record[0x27]==1,"The fortieth militia call advances progress exactly once");
            var beforeCancel=battle.Resources;
            Check(battle.CancelRecruitment() && battle.RecruitmentCount==0 && battle.Resources==new ResourceQuad(beforeCancel.First+1,beforeCancel.Second,beforeCancel.Third+4,beforeCancel.Fourth+1),
                "Militia cancellation uses its DAT progress limit and removes unpaid tails");
        }
        using (var battle=CreateFundedSkirmish())
        {
            var stocks=battle.Resources;battle.QueueWorker();battle.Advance(1.0/battle.Rules.TicksPerSecond);
            Check(battle.CancelRecruitment() && battle.Resources==new ResourceQuad(stocks.First-5,stocks.Second,stocks.Third-40,stocks.Fourth),
                "Zero-progress cancellation returns the person and no materials");
            stocks=battle.Resources;battle.QueueWorker();battle.Advance(1.0/battle.Rules.TicksPerSecond);
            for(int i=0;i<90;i++)battle.Advance(1.0/battle.Rules.TicksPerSecond);
            Check(battle.PlayerCastle.Record[0x27]==3,"Cancellation sample reaches three of five source progress steps");
            Check(battle.CancelRecruitment() && battle.Resources==new ResourceQuad(stocks.First-2,stocks.Second,stocks.Third-16,stocks.Fourth),
                "Partial worker cancellation applies native progress refund and restores the full person");
        }
        var scene=GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>();AddChild(scene);
        for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        scene.SelectCastle();scene.HireWorker();scene.HireMilitia();
        Check(scene.Battle.RecruitmentCount==2 && scene.Selection.Single()==scene.Battle.PlayerCastle.Id,"Castle buttons append orders and retain castle selection");
        if(DisplayServer.GetName()!="headless")
        {
            scene._Process(0);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));GetViewport().GetTexture().GetImage().SavePng("res://build/recruitment-queue.png");
        }
        scene.CancelRecruitment();Check(scene.Battle.RecruitmentCount==0,"UI clear command removes the waiting orders");RemoveChild(scene);scene.Free();
    }
}
