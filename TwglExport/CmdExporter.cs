#region Namespaces
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
#endregion

namespace TwglExport
{
  [Transaction( TransactionMode.ReadOnly )]
  public class CmdExporter : IExternalCommand
  {
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;
      Document doc = uidoc.Document;
      Selection sel = uidoc.Selection;
      ICollection<ElementId> ids = sel.GetElementIds();

      if( 1 != ids.Count )
      {
        message = "Please select an element to export to TWGL.";
        return Result.Failed;
      }

      Element e = null;

      foreach( ElementId id in ids )
      {
        e = doc.GetElement( id );
      }

      // Determine bounding box in order to translate
      // all coordinates to bounding box midpoint.

      BoundingBoxXYZ bb = e.get_BoundingBox( null );
      XYZ pmin = bb.Min;
      XYZ pmax = bb.Max;
      XYZ vsize = pmax - pmin;
      XYZ pmid = pmin + 0.5 * vsize;

      Options opt = new Options();
      GeometryElement geo = e.get_Geometry( opt );

      List<int> faceIndices = new List<int>();
      List<int> faceVertices = new List<int>();
      List<double> faceNormals = new List<double>();
      int[] triangleIndices = new int[3];
      XYZ[] triangleCorners = new XYZ[3];

      foreach( GeometryObject obj in geo )
      {
        Solid solid = obj as Solid;

        if( solid != null && 0 < solid.Faces.Size )
        {
          faceIndices.Clear();
          faceVertices.Clear();
          faceNormals.Clear();

          foreach( Face face in solid.Faces )
          {
            Mesh mesh = face.Triangulate();

            int nTriangles = mesh.NumTriangles;

            IList<XYZ> vertices = mesh.Vertices;

            int nVertices = vertices.Count;

            List<int> vertexCoordsMm = new List<int>( 3 * nVertices );

            // A vertex may be reused several times with 
            // different normals for different faces, so 
            // we cannot precalculate normals per vertex.
            //List<double> normals = new List<double>( 3 * nVertices );

            foreach( XYZ v in vertices )
            {
              // Translate the entire element geometry
              // to the bounding box midpoint and scale 
              // to metric millimetres.

              XYZ p = v - pmid;

              vertexCoordsMm.Add( Util.FootToMm( p.X ) );
              vertexCoordsMm.Add( Util.FootToMm( p.Y ) );
              vertexCoordsMm.Add( Util.FootToMm( p.Z ) );
            }

            for( int i = 0; i < nTriangles; ++i )
            {
              MeshTriangle triangle = mesh.get_Triangle( i );

              for( int j = 0; j < 3; ++j )
              {
                int k = (int) triangle.get_Index( j );
                triangleIndices[j] = k;
                triangleCorners[j] = vertices[k];
              }

              // Calculate constant triangle facet normal.

              XYZ v = triangleCorners[1]
                - triangleCorners[0];
              XYZ w = triangleCorners[2]
                - triangleCorners[0];
              XYZ triangleNormal = v
                .CrossProduct( w )
                .Normalize();

              for( int j = 0; j < 3; ++j )
              {
                int nFaceVertices = faceVertices.Count;

                Debug.Assert( nFaceVertices.Equals( faceNormals.Count ),
                  "expected equal number of face vertex and normal coordinates" );

                faceIndices.Add( nFaceVertices / 3 );

                int i3 = triangleIndices[j] * 3;

                // Rotate the X, Y and Z directions, 
                // since the Z direction points upward 
                // in Revit as opposed to sideways or
                // outwards or forwards in WebGL.

                faceVertices.Add( vertexCoordsMm[i3 + 1] );
                faceVertices.Add( vertexCoordsMm[i3 + 2] );
                faceVertices.Add( vertexCoordsMm[i3] );

                if( CmdElemGeom.RetainCurvedSurfaceFacets )
                {
                  faceNormals.Add( triangleNormal.Y );
                  faceNormals.Add( triangleNormal.Z );
                  faceNormals.Add( triangleNormal.X );
                }
                else
                {
                  UV uv = face.Project(
                    triangleCorners[j] ).UVPoint;

                  XYZ normal = face.ComputeNormal( uv );

                  faceNormals.Add( normal.Y );
                  faceNormals.Add( normal.Z );
                  faceNormals.Add( normal.X );
                }
              }
            }
          }

          // Scale the vertices to a [-1,1] cube 
          // centered around the origin. Translation
          // to the origin was already performed above.

          double scale = 2.0 / Util.FootToMm( Util.MaxCoord( vsize ) );

          string sposition = string.Join( ", ",
            faceVertices.ConvertAll<string>(
              i => ( i * scale ).ToString( "0.##" ) ) );

          string snormal = string.Join( ", ",
            faceNormals.ConvertAll<string>(
              f => f.ToString( "0.##" ) ) );

          string sindices = string.Join( ", ",
            faceIndices.ConvertAll<string>(
              i => i.ToString() ) );

          Debug.Print( "position: [{0}],", sposition );
          Debug.Print( "normal: [{0}],", snormal );
          Debug.Print( "indices: [{0}],", sindices );

          //string json_geometry_data = string.Format(
          //  "{ \"position\": [{0}],\n\"normal\": [{1}], \"indices\": [{2}] }",
          //  sposition, snormal, sindices );

          string json_geometry_data =
            "{ \"position\": [" + sposition
            + "],\n\"normal\": [" + snormal
            + "],\n\"indices\": [" + sindices
            + "] }";

          Debug.Print( "json: " + json_geometry_data );

          CmdElemGeom.DisplayWgl( json_geometry_data );
        }
      }
      return Result.Succeeded;
    }
  }
}