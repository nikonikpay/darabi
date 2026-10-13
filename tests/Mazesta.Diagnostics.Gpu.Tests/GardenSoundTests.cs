using System.Numerics; using Xunit;
using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class GardenSoundTests
{
    private static readonly Vector3 Fountain = GardenScene.Embedded.Fountain!.Nozzle;

    private static double Rms(SoundLevels levels, int seconds = 6)
    {
        var mix = new GardenSoundMix(); mix.Set(levels); var pcm = new short[GardenSoundMix.Rate * 2]; double sum = 0; long n = 0;
        for (int s = 0; s < seconds; s++) { mix.Render(pcm, GardenSoundMix.Rate); if (s >= 2) foreach (short v in pcm) { sum += (double)v * v; n++; } }   // the first two seconds are the fade-in
        return Math.Sqrt(sum / n) / short.MaxValue;
    }

    [Fact] public void The_hall_has_music_and_the_garden_by_the_fountain_has_water()
    {
        var hall = SoundLevels.At(new(0, 2.7f, 1), Fountain, 10); var pool = SoundLevels.At(new(Fountain.X + 2, 1.7f, Fountain.Z), Fountain, 10); var far = SoundLevels.At(new(8.7f, 1.7f, -31), Fountain, 10);
        Assert.Equal(1, hall.Music); Assert.Equal(1, hall.Indoor); Assert.True(hall.Water < 0.25f);
        Assert.True(pool.Water > 0.3f); Assert.Equal(0, pool.Music); Assert.Equal(0, pool.Indoor);
        Assert.Equal(0, far.Water);
    }

    [Fact] public void Music_comes_faintly_before_the_door_and_is_gone_by_the_pool()
    {
        float At(float z) => SoundLevels.At(new(0, 1.7f, z), Fountain, 10).Music;
        Assert.True(At(-4) > At(-8)); Assert.True(At(-8) > At(-12) || At(-12) == 0); Assert.True(At(-8) > 0); Assert.True(At(-3.2f) < 1); Assert.Equal(1, At(-2)); Assert.Equal(0, At(-15)); Assert.Equal(0, At(-19));
    }

    [Fact] public void In_the_hall_there_is_only_the_music() => Assert.Equal(0, Rms(new SoundLevels(0, 0, 1, 1)));

    [Fact] public void The_birds_sing_by_day_and_not_by_night()
    {
        Assert.Equal(1, SoundLevels.At(new(0, 1.7f, -20), Fountain, GardenDay.Sunrise + 40).Day); Assert.Equal(0, SoundLevels.At(new(0, 1.7f, -20), Fountain, 0.2f + GardenCamera.Loop * 0.95f).Day, 0.01);
    }

    [Fact] public void The_mix_is_audible_where_it_should_be_and_never_clips()
    {
        var garden = new SoundLevels(0, 0.8f, 1, 0); var hall = new SoundLevels(1, 0, 0, 1);
        Assert.InRange(Rms(garden), 0.003, 0.5); Assert.InRange(Rms(hall), 0.003, 0.5);
        var mix = new GardenSoundMix(); mix.Set(new(1, 1, 1, 0)); var pcm = new short[GardenSoundMix.Rate]; mix.Render(pcm, GardenSoundMix.Rate / 2);
        Assert.All(pcm, v => Assert.InRange(v, (short)-32000, (short)32000));
    }

    [Fact] public void The_piece_fades_in_at_its_start_and_out_at_its_end_and_the_night_has_no_music()
    {
        var mix = new GardenSoundMix(); mix.Set(new(1, 0, 0, 1)); var pcm = new short[GardenSoundMix.Rate * 2]; var peaks = new int[8];
        for (int sec = 0; sec < 8; sec++) { mix.Render(pcm, GardenSoundMix.Rate); peaks[sec] = pcm.Max(v => Math.Abs((int)v)); }
        Assert.True(peaks[0] < peaks[7] / 2, "the piece begins faint and swells");
        Assert.True(Rms(new SoundLevels(0, 0, 0, 0)) < Rms(new SoundLevels(0, 0, 1, 0)));   // night is quieter than day
    }

    [Fact] public void Indoors_the_garden_is_quieter_than_outside_it()
        => Assert.True(Rms(new SoundLevels(0, 0, 1, 1)) < Rms(new SoundLevels(0, 0, 1, 0)));

    [Fact] public void The_owners_piece_is_embedded_and_stretched_to_the_mixs_rate()
    {
        var pcm = GardenMusic.Load(); Assert.Equal(0, pcm.Length % 2); Assert.True(pcm.Length / 2 / (double)GardenSoundMix.Rate > 30); Assert.Contains(pcm, v => Math.Abs((int)v) > 5000);
    }

    [Fact] public void The_day_is_the_owners_recording_of_birds_and_leaves_and_it_loops_without_a_jump()
    {
        var pcm = GardenMusic.Load("garden-ambience.mp3"); Assert.True(pcm.Length / 2 / (double)GardenSoundMix.Rate > 120);
        var mix = new GardenSoundMix(); mix.Set(new(0, 0, 1, 0)); var buf = new short[GardenSoundMix.Rate * 2]; int max = 0;
        for (int sec = 0; sec < 5; sec++) { mix.Render(buf, GardenSoundMix.Rate); max = Math.Max(max, buf.Max(v => Math.Abs((int)v))); }
        Assert.InRange(max, 50, 30000);
    }
}
