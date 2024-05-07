# SharpAstrology.HumanDesign.BlazorComponents - A Blazor component library for SharpAstrology.HumanDesign

```C#
@using SharpAstrology.DataModels
@using SharpAstrology.Ephemerides
@using SharpAstrology.HumanDesign.BlazorComponents

    
<HumanDesignGraph Chart="chart" Height="800px"/>


@code
{
    [Inject] SwissEphemeridesService EphService { get; set; }
    private HumanDesignChart chart;
    
    protected override void OnInitialized()
    {
        using var eph = EphService.CreateContext();
        chart = new HumanDesignChart(new DateTime(1988, 9, 4, 1, 15, 0, DateTimeKind.Utc), eph);
    }
}
```