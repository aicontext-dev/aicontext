param(
    [string]$ContextFile = ".aicontext/context.json",
    [string]$ReadmeFile = ".aicontext/README.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$validationErrors = 0
$allowedTargets = @("agents", "copilot", "claude", "cursor", "windsurf", "aider")
$allowedDefaultTargets = $allowedTargets + @("all")

function Add-ValidationError {
    param([string]$Message)
    Write-Host "::error::$Message"
    $script:validationErrors++
}

function Has-Property {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)] [string]$Name
    )

    return $null -ne $Object.PSObject.Properties[$Name]
}

if (-not (Test-Path -LiteralPath $ReadmeFile)) {
    Add-ValidationError "Missing required file: $ReadmeFile"
}
else {
    $readmeContent = Get-Content -LiteralPath $ReadmeFile -Raw
    if ([string]::IsNullOrWhiteSpace($readmeContent)) {
        Add-ValidationError "$ReadmeFile is empty."
    }
}

$context = $null
if (-not (Test-Path -LiteralPath $ContextFile)) {
    Add-ValidationError "Missing required file: $ContextFile"
}
else {
    try {
        $context = Get-Content -LiteralPath $ContextFile -Raw | ConvertFrom-Json
    }
    catch {
        Add-ValidationError "Invalid JSON in $ContextFile. $($_.Exception.Message)"
    }
}

if ($null -ne $context) {
    if (-not (Has-Property -Object $context -Name "specVersion")) {
        Add-ValidationError "context.json is missing required property: specVersion"
    }
    elseif ($context.specVersion -ne "0.1.0") {
        Add-ValidationError "specVersion must be '0.1.0' for the current schema."
    }

    if (-not (Has-Property -Object $context -Name "project")) {
        Add-ValidationError "context.json is missing required object: project"
    }
    else {
        $project = $context.project
        if (-not (Has-Property -Object $project -Name "name")) {
            Add-ValidationError "project.name is required."
        }
        elseif ([string]::IsNullOrWhiteSpace([string]$project.name)) {
            Add-ValidationError "project.name must be a non-empty string."
        }
    }

    if (Has-Property -Object $context -Name "exports") {
        $exports = $context.exports
        $targetList = @()

        if (Has-Property -Object $exports -Name "targets") {
            if ($exports.targets -isnot [System.Array]) {
                Add-ValidationError "exports.targets must be an array."
            }
            else {
                $targetList = @($exports.targets)
                $duplicates = $targetList | Group-Object | Where-Object { $_.Count -gt 1 }
                if ($duplicates) {
                    Add-ValidationError "exports.targets contains duplicate entries."
                }

                foreach ($target in $targetList) {
                    if ($target -notin $allowedTargets) {
                        Add-ValidationError "exports.targets contains unsupported target '$target'."
                    }
                }
            }
        }

        if (Has-Property -Object $exports -Name "defaultTarget") {
            $defaultTarget = [string]$exports.defaultTarget
            if ($defaultTarget -notin $allowedDefaultTargets) {
                Add-ValidationError "exports.defaultTarget must be one of: $($allowedDefaultTargets -join ", ")."
            }
            elseif ($targetList.Count -gt 0 -and $defaultTarget -ne "all" -and $defaultTarget -notin $targetList) {
                Add-ValidationError "exports.defaultTarget must be 'all' or one of exports.targets."
            }
        }
    }

    if (Has-Property -Object $context -Name "metadata") {
        $metadata = $context.metadata
        if (Has-Property -Object $metadata -Name "lastReviewed") {
            $reviewDate = [string]$metadata.lastReviewed
            $parsedDate = [DateTime]::MinValue
            $isValidDate = [DateTime]::TryParseExact(
                $reviewDate,
                "yyyy-MM-dd",
                [System.Globalization.CultureInfo]::InvariantCulture,
                [System.Globalization.DateTimeStyles]::None,
                [ref]$parsedDate
            )
            if (-not $isValidDate) {
                Add-ValidationError "metadata.lastReviewed must use ISO date format yyyy-MM-dd."
            }
        }
    }
}

if ($validationErrors -gt 0) {
    Write-Host "Validation failed with $validationErrors error(s)."
    exit 1
}

Write-Host "AIContext validation passed."
