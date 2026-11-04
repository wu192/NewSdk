[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile("$root/recovery/scripts/TestCompilerXmlMatrices.ps1",[ref]$null,[ref]$errors)
if($errors.Count){throw ($errors.Message -join '; ')}
$functions=@($ast.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Check-XmlMatrix'},$true))
if($functions.Count -ne 1){throw 'Expected exactly one actual XML matrix evidence guard'}
Invoke-Expression $functions[0].Extent.Text
$work=Join-Path ([IO.Path]::GetTempPath()) ('xml-matrix-guards-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work|Out-Null
$passed=0;$rejected=0
$contracts=@{}
function Assert-Rejected([scriptblock]$Action,[string]$Name) {
    $threw=$false
    try {& $Action|Out-Null}catch{$threw=$true}
    if(!$threw){throw "Matrix guard accepted invalid evidence: $Name"}
    $script:rejected++
}
function Write-MatrixFixture([string]$Group,[string]$Format,[int]$Fixtures,[int]$Comparisons,[int]$Rejections,[int]$Layouts) {
    $log=Join-Path $work "$Group.log";$wire=Join-Path $work "$Group.tsv"
    $lines=@()
    foreach($p in @(0,1,2,99)) {
        switch($Format) {
            'dual' {$lines+="PASS platform=$p fixtures=$Fixtures threeArrayComparisons=$($Comparisons/2) directArrayComparisons=$($Comparisons/2) rejections=$Rejections independentLayouts=$Layouts"}
            'attributes' {$lines+="SUMMARY platform=$p fixtures=$Fixtures threeArrayComparisons=$Comparisons rejected=$Rejections independentLayouts=$Layouts"}
            'powers' {$lines+="PASS platform=$p fixtures=$Fixtures comparisons=$Comparisons rejections=$Rejections independent=$Layouts"}
            default {$lines+="PASS platform=$p fixtures=$Fixtures arrayComparisons=$Comparisons rejections=$Rejections layoutAssertions=$Layouts"}
        }
    }
    $count=4*$Fixtures;$lines+="ORACLE-END calls=$count"
    [IO.File]::WriteAllLines($log,[string[]]$lines)
    $rows=[Collections.Generic.List[string]]::new();$hash='a'*64;$i=0
    foreach($p in @(0,1,2,99)){for($j=0;$j -lt $Fixtures;$j++){$i++;$rows.Add("$i`t$p`t$hash`t$hash`t$hash`t$hash")}}
    $rows.Add("END`t$count");[IO.File]::WriteAllLines($wire,$rows)
    return @{log=$log;wire=$wire;group=$Group}
}
try {
    $excluded=@('XmlNodeOracle.cs','XmlPriorityLinkOracle.cs','XmlPrioritySdkOracle.cs')
    $oracles=@(Get-ChildItem "$root/recovery/tests/Xml*Oracle.cs"|Where-Object{$_.Name -notin $excluded}|Sort-Object Name)
    if($oracles.Count -ne 64){throw 'Expected all 64 group contracts'}
    foreach($oracle in $oracles) {
        $group=$oracle.BaseName -replace 'Oracle$',''
        $old=Get-Content "$root/recovery/scripts/Test$group.ps1" -Raw
        $f=2;$r=1;$l=2;$format='standard';$ratio=0
        # Discover the existing script's exact count contract instead of
        # assuming all groups expose the same recovered/direct API paths.
        if($old -match 'arrayComparisons -ne ([36])\*\$row\[0\]\.fixtures') {$ratio=[int]$Matches[1]}
        elseif($old -match 'threeArrayComparisons -ne 3\*\$p\.fixtures' -and $old -match 'directArrayComparisons -ne 3\*\$p\.fixtures') {$format='dual';$ratio=6}
        elseif($group -eq 'Xmlpowers') {$format='powers';$ratio=3}
        elseif($group -eq 'Xmlattributes') {$format='attributes';$ratio=3}
        elseif($group -eq 'Xmlplayers') {$ratio=3;$f=1000;$r=30;$l=300}
        elseif($group -eq 'Xmlai') {$ratio=6;$f=1000;$r=30;$l=300}
        else {throw "Unclassified existing matrix contract: $group"}
        $fixture=Write-MatrixFixture $group $format $f ($ratio*$f) $r $l
        $result=Check-XmlMatrix $fixture.log $fixture.wire $group
        if($result.platforms.Count -ne 4 -or $result.originalCalls -ne 4*$f){throw "Valid matrix guard receipt malformed: $group"}
        $contracts[$group]=@{format=$format;ratio=$ratio;fixtures=$f;rejections=$r;layouts=$l}
        $passed++
    }
    if($contracts['Xmllocomotor'].ratio -ne 3 -or $contracts['Xmlfxparticle'].ratio -ne 3){throw 'Original single-path contracts were lost'}
    # Attributes metadata checks compare an original baseline as well; AI's
    # prefix probes deliberately have only one three-buffer comparison path.
    $fixture=Write-MatrixFixture 'Xmlattributes' 'attributes' 2 9 1 1
    Check-XmlMatrix $fixture.log $fixture.wire $fixture.group|Out-Null
    $fixture=Write-MatrixFixture 'Xmlai' 'standard' 1001 6003 30 300
    Check-XmlMatrix $fixture.log $fixture.wire $fixture.group|Out-Null
    foreach($group in @('Xmllocomotor','Xmlfxparticle','Xmlpowers','Xmlabilitybuttons')) {
        $c=$contracts[$group];$wrong=if($c.ratio -eq 3){6}else{3}
        $fixture=Write-MatrixFixture $group $c.format $c.fixtures ($wrong*$c.fixtures) $c.rejections $c.layouts
        Assert-Rejected {Check-XmlMatrix $fixture.log $fixture.wire $group} "$group wrong comparison count"
    }
    $fixture=Write-MatrixFixture 'Xmlabilitybuttons' 'standard' 2 12 0 2
    Assert-Rejected {Check-XmlMatrix $fixture.log $fixture.wire $fixture.group} 'missing rejection coverage'
    $fixture=Write-MatrixFixture 'Xmlabilitybuttons' 'standard' 2 12 1 1
    Assert-Rejected {Check-XmlMatrix $fixture.log $fixture.wire $fixture.group} 'missing layout coverage'
    foreach($group in @('Xmlplayers','Xmlai')) {
        $ratio=$contracts[$group].ratio
        $fixture=Write-MatrixFixture $group 'standard' 1000 ($ratio*1000) 29 300
        Assert-Rejected {Check-XmlMatrix $fixture.log $fixture.wire $fixture.group} "$group below original rejection threshold"
    }
    $fixture=Write-MatrixFixture 'Xmlaudio' 'dual' 2 12 1 2
    $lines=[IO.File]::ReadAllLines($fixture.log);$lines[0]=$lines[0] -replace 'directArrayComparisons=6','directArrayComparisons=3';[IO.File]::WriteAllLines($fixture.log,$lines)
    Assert-Rejected {Check-XmlMatrix $fixture.log $fixture.wire $fixture.group} 'missing direct comparison coverage'
    $fixture=Write-MatrixFixture 'Xmlabilitybuttons' 'standard' 2 12 1 2
    [IO.File]::WriteAllLines($fixture.wire,[string[]]@((Get-Content $fixture.wire)[0..7]))
    Assert-Rejected {Check-XmlMatrix $fixture.log $fixture.wire $fixture.group} 'missing oracle terminal'
    $fixture=Write-MatrixFixture 'Xmlabilitybuttons' 'standard' 2 12 1 2
    $rows=[IO.File]::ReadAllLines($fixture.wire);$rows[0]=$rows[0] -replace '^1','2';[IO.File]::WriteAllLines($fixture.wire,$rows)
    Assert-Rejected {Check-XmlMatrix $fixture.log $fixture.wire $fixture.group} 'reordered oracle sequence'
    Write-Host "PASS XML matrix guard: all $passed existing group count contracts plus 2 irregular valid counts accepted; $rejected malformed receipts rejected (actual extracted production guard)"
} finally {Remove-Item $work -Recurse -Force}
