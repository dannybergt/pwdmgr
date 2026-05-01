function Get-PwdmgrAgentStatus {
  [CmdletBinding()]
  param()

  [pscustomobject]@{
    Product = 'Privora'
    Project = 'pwdmgr'
    Status = 'Bootstrap'
  }
}

