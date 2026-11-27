<?xml version="1.0" encoding="utf-8"?>
<!-- Takes the Recipe API's .exe out of what Heat harvests from its publish (the RecipeApiFiles component group): a service
     runs the file its component's key path is, so Package.wxs authors that file itself, in the RecipeApiService component
     with the service and its firewall rule. Everything else Heat harvested is copied as it is. -->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:wix="http://wixtoolset.org/schemas/v4/wxs"
                exclude-result-prefixes="wix">

  <xsl:output method="xml" indent="yes" />

  <!-- The components holding the .exe, by their Id, for their ComponentRefs too: a File whose Source ends in
       \TinapayanRMS.RecipeApi.exe, 27 characters (XSLT 1.0 has no ends-with, and a key's pattern can't name a variable). -->
  <xsl:key name="exeComponents"
           match="wix:Component[wix:File[substring(@Source, string-length(@Source) - 26) = '\TinapayanRMS.RecipeApi.exe']]"
           use="@Id" />

  <xsl:template match="@* | node()">
    <xsl:copy>
      <xsl:apply-templates select="@* | node()" />
    </xsl:copy>
  </xsl:template>

  <xsl:template match="wix:Component[key('exeComponents', @Id)]" />
  <xsl:template match="wix:ComponentRef[key('exeComponents', @Id)]" />

</xsl:stylesheet>
