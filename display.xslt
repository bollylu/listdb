<?xml version="1.0" encoding="ISO-8859-1"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"> 


<xsl:template match="/">
  <html>
  <body>
    <h2>Databases list</h2>
    <table border="1" width="95%" align="center">
    <tr bgcolor="#9acd32">
      <th width="50%" align="left">Name</th>
      <th width="50%" align="left">Size (MB)</th>
    </tr>
    <xsl:for-each select="DumpSql/Databases">
    <tr>
      <td width="50%"><xsl:value-of select="name"/></td>
      <td width="50%" align="right"><xsl:value-of select="size"/></td>
    </tr>
    </xsl:for-each>
    </table>
    
    <h2>Tables list</h2>
    <table border="1" width="95%" align="center">
    <tr bgcolor="#9acd32">
      <th width="70%" align="left">Table name</th>
      <th width="30%" align="left">Data space used</th>
    </tr>
    <xsl:for-each select="DumpSql/Tables">
    <tr>
      <td width="70%"><xsl:value-of select="tablename"/></td>
      <td width="30%" align="right"><xsl:value-of select="DataSpaceUsed"/></td>
    </tr>
    <tr>
      <td width="100%" colspan="2"><PRE><xsl:value-of select="Script"/></PRE></td>
    </tr>
    </xsl:for-each>
    </table>
    
  </body>
  </html>
</xsl:template>


</xsl:stylesheet>

  