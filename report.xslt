<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:template match="/">
    <html>
      <head>
        <title>Job report</title>
      </head>
      <body>
        <table border="1" width="100%">
          <tr bgcolor="#9acd32">
            <th>Server</th>
            <th>Category</th>
            <th>Job</th>
            <th>Description</th>
            <th>Owner</th>
          </tr>
          <xsl:for-each select="/root/server">
            <tr>
              <td colspan="5" bgcolor="yellow">
                <xsl:attribute select="server" />
              </td>
            </tr>
            <xsl:for-each select="/root/server/jobs/job">
              <xsl:sort select="category" />
              <xsl:sort select="name" />
              <tr>
                <td>
                  <xsl:value-of select="server" />
                </td>
                <td>
                  <xsl:value-of select="category" />
                </td>
                <td>
                  <xsl:value-of select="name" />
                </td>
                <td>
                  <xsl:value-of select="description" />
                </td>
                <td>
                  <xsl:value-of select="owner" />
                </td>
              </tr>
            </xsl:for-each>
          </xsl:for-each>
        </table>
      </body>
    </html>
  </xsl:template>
</xsl:stylesheet>