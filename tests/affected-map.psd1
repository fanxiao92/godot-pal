@{
    Version = 1
    SmokeGameCategory = 'Smoke'
    TestProjects = @{
        'tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj' = 'core-unit'
        'tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj' = 'extract-unit'
        'tests/Pal.Extract.IntegrationTests/Pal.Extract.IntegrationTests.csproj' = 'extract-integration'
        'tests/Pal.Prepare.Tests/Pal.Prepare.Tests.csproj' = 'prepare'
        'tests/Pal.Game.Tests/Pal.Game.Tests.csproj' = 'game:*'
    }
    Rules = @(
        # 1) Mandatory full fallback: build graph, SDK/NuGet, runsettings, Godot project config, front door, CI.
        @{
            Name = 'build-and-infra-full'
            Paths = @(
                '*.sln',
                '*.slnx',
                '**/*.csproj',
                '**/Directory.Build.*',
                'Directory.Packages.props',
                'global.json',
                'NuGet.Config',
                '.config/dotnet-tools.json',
                '**/*.runsettings',
                'src/Pal.Game/project.godot',
                'src/Pal.Game/export_presets.cfg',
                'src/Pal.Game/addons/**',
                'tests/Pal.Game.Tests/project.godot',
                '.gitattributes',
                '.gitignore',
                'scripts/**',
                'pal.ps1',
                'tests/affected-map.psd1',
                '.github/**'
            )
            Action = 'full'
        }

        # 2) Pure documentation & read-only reference: no-op.
        @{
            Name = 'docs-and-reference-noop'
            Paths = @(
                'docs/**',
                '*.md',
                '**/*.md',
                'reference/**'
            )
            Action = 'none'
        }

        # 3) Long-running agent (Node/TS) -> dedicated LRA test target.
        @{
            Name = 'lra-tests'
            Paths = @('tools/long-running-agent/**')
            Targets = @('lra')
        }

        # 4) Production .NET projects -> expanded via static .csproj reverse ProjectReference closure.
        @{
            Name = 'project-pal-core'
            Paths = @('src/Pal.Core/**')
            Project = 'src/Pal.Core/Pal.Core.csproj'
        }
        @{
            Name = 'project-pal-extract'
            Paths = @('tools/Pal.Extract/**')
            Project = 'tools/Pal.Extract/Pal.Extract.csproj'
        }

        # 5) Direct .NET test projects.
        @{
            Name = 'tests-core-unit'
            Paths = @('tests/Pal.Core.UnitTests/**')
            Targets = @('core-unit')
        }
        @{
            Name = 'tests-extract-unit'
            Paths = @('tests/Pal.Extract.UnitTests/**')
            Targets = @('extract-unit')
        }
        @{
            Name = 'tests-extract-integration'
            Paths = @('tests/Pal.Extract.IntegrationTests/**')
            Targets = @('extract-integration')
        }
        @{
            Name = 'tests-prepare'
            Paths = @('tests/Pal.Prepare.Tests/**')
            Targets = @('prepare')
        }

        # 6) Cross-directory linked Compile fixtures (verified against MSBuild -getItem:Compile).
        @{
            Name = 'linked-battle-context-fixture'
            Paths = @('tests/Fixtures/BattleContextTestFixture.cs')
            Targets = @('core-unit', 'prepare', 'game:*')
        }
        @{
            Name = 'linked-prepare-fixtures'
            Paths = @(
                'tests/Fixtures/M1Bundle.cs',
                'tests/Fixtures/ProjectedStoryChainTables.cs'
            )
            Targets = @('prepare')
        }
        @{
            Name = 'linked-yj2-fixture'
            Paths = @('tests/Pal.Extract.Tests/Fixtures/Yj2TestData.cs')
            Targets = @('extract-unit', 'extract-integration')
        }
        @{
            Name = 'linked-battle-projection-fixture'
            Paths = @('tests/Pal.Extract.UnitTests/Fixtures/BattleProjectionFixture.cs')
            Targets = @('extract-unit', 'extract-integration')
        }

        # 7) Godot content & scripts (not MSBuild items) + M4 native-map/profiles guard.
        @{
            Name = 'godot-profiles-and-samples'
            Paths = @(
                'src/Pal.Game/Scripts/Profiles/**',
                'src/Pal.Game/Scripts/Resources/**',
                'src/Pal.Game/Editor/**',
                'src/Pal.Game/Samples/**',
                'samples/authored-battle/**'
            )
            Targets = @('game:Profiles', 'game:Map', 'prepare')
        }
        @{
            Name = 'godot-game-production'
            Paths = @('src/Pal.Game/**')
            Targets = @('game:*', 'prepare')
        }
        @{
            Name = 'godot-game-tests'
            Paths = @('tests/Pal.Game.Tests/**')
            Targets = @('game:*')
            NarrowGameTestCategory = $true
        }
    )
}
