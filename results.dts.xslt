<?xml version="1.0" encoding="UTF-8" ?>
<stylesheet version="1.0" xmlns="http://www.w3.org/1999/XSL/Transform">
</stylesheet>

<xsd:template match="/">
  <html>
  <body>
    <h2>List of packages</h2>
    <table border="1">
    <tr bgcolor="#9acd32">
      <th align="left">Name</th>
      <th align="left">Sent to</th>
      <th align="left">Attachments</th>
    </tr>
    <xsl:for-each select="NewDataSet/DTS.Packages">
    <tr>
      <td><xsl:value-of select="Name"/></td>
      <td><xsl:value-of select="EmailTo"/></td>
      <td><xsl:value-of select="EmailAttach"/></td>
    </tr>
    </xsl:for-each>
    </table>
  </body>
  </html>
</xsl:template>

</stylesheet>