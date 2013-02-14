﻿using System;
using System.Data.Entity.Infrastructure;
using Unified;

namespace Terminals.Data.DB
{
    internal partial class DbFavorite : IFavorite
    {
        /// <summary>
        /// Manages lazy loading and caching of the favorite extended properties, which are modeled
        /// as referenced entities. Also handles manual loading/saving of icon and favorite protocol properties.
        /// </summary>
        internal class FavoriteDetails
        {
            private readonly DbFavorite favorite;
            internal DbSecurityOptions Security { get; private set; }
            internal DbBeforeConnectExecute ExecuteBeforeConnect { get; private set; }
            internal DbDisplayOptions Display { get; private set; }

            private bool protocolPropertiesLoaded;

            internal bool Loaded
            {
                get
                {
                    return this.Security != null;
                }
            }

            private bool ShouldSaveIcon
            {
                get
                {
                    return this.favorite.toolBarIcon != null &&
                           !FavoriteIcons.IsDefaultProtocolImage(this.favorite.toolBarIcon);
                }
            }

            internal FavoriteDetails(DbFavorite favorite)
            {
                this.favorite = favorite;
            }

            internal void Load()
            {
                if (!this.Loaded && !this.favorite.isNewlyCreated)
                    this.LoadDetailsFromDatabase();
            }

            private void LoadDetailsFromDatabase()
            {
                try
                {
                    this.TryLoadDetailsFromDatabase();
                }
                catch (Exception exception)
                {
                    Logging.Log.Error("Unable to load favorite details from database" + this.favorite, exception);
                    ReleaseReferences(); // rollback loading
                }
            }

            private void TryLoadDetailsFromDatabase()
            {
                using (Database database = DatabaseConnections.CreateInstance())
                {
                    database.Favorites.Attach(this.favorite);
                    this.LoadReferences(database);
                    this.LoadFieldsFromReferences();
                    database.Cache.DetachFavorite(this.favorite);
                }
            }

            internal void LoadFieldsFromReferences()
            {
                this.Security = this.favorite.Security;
                this.Security.AssignStores(this.favorite.credentials, this.favorite.persistenceSecurity);
                this.Security.LoadFieldsFromReferences();
                this.Display = this.favorite.Display;
                this.ExecuteBeforeConnect = this.favorite.ExecuteBeforeConnect;
            }

            private void LoadReferences(Database database)
            {
                DbEntityEntry<DbFavorite> favoriteEntry = database.Entry(this.favorite);
                favoriteEntry.Reference(f => f.Security).Load();
                // we have to load security references after the parent references is loaded
                this.favorite.Security.LoadReferences(database);
                favoriteEntry.Reference(f => f.Display).Load();
                favoriteEntry.Reference(f => f.ExecuteBeforeConnect).Load();
            }

            internal void Save(Database database)
            {
                // partial save possible of of next methods, try to save as much as possible
                this.SaveProtocolProperties(database);
                this.UpdateImageInDatabase(database);
            }

            /// <summary>
            /// Using given database context commits changes of protocol properties into the database
            /// </summary>
            private void SaveProtocolProperties(Database database)
            {
                try
                {
                    string serializedProperties = Serialize.SerializeXMLAsString(this.favorite.protocolProperties);
                    database.UpdateFavoriteProtocolProperties(this.favorite.Id, serializedProperties);
                    this.favorite.isNewlyCreated = false;
                }
                catch (Exception exception)
                {
                    Logging.Log.Error("Couldn't save protocol properties to database", exception);
                }
            }

            private void UpdateImageInDatabase(Database database)
            {
                try
                {
                    this.TryUpdateImageInDatabase(database);
                }
                catch (Exception exception)
                {
                    Logging.Log.Error("Couldn't load image from database", exception);
                }
            }

            private void TryUpdateImageInDatabase(Database database)
            {
                if (this.ShouldSaveIcon)
                {
                    byte[] imageData = FavoriteIcons.ImageToBinary(this.favorite.toolBarIcon);
                    if (imageData.Length > 0)
                    {
                        database.SetFavoriteIcon(this.favorite.Id, imageData);
                    }
                }
            }

            /// <summary>
            /// Realization of expensive property lazy loading
            /// </summary>
            internal void LoadProtocolProperties()
            {
                try
                {
                    this.TryLoadPotocolProperties();
                }
                catch (Exception exception)
                {
                    Logging.Log.Error("Couldn't obtain protocol properties from database", exception);
                    this.favorite.protocolProperties = Favorite.UpdateProtocolPropertiesByProtocol(
                      this.favorite.Protocol, new RdpOptions());
                }
            }

            private void TryLoadPotocolProperties()
            {
                if (!this.favorite.isNewlyCreated && !this.protocolPropertiesLoaded)
                {
                    this.LoadPropertiesFromDatabase();
                    this.favorite.AssignStoreToRdpOptions(this.favorite.persistenceSecurity);
                    this.protocolPropertiesLoaded = true;
                }
            }

            private void LoadPropertiesFromDatabase()
            {
                using (Database database = DatabaseConnections.CreateInstance())
                {
                    string serializedProperties = database.GetProtocolPropertiesByFavorite(this.favorite.Id);
                    Type propertiesType = this.favorite.protocolProperties.GetType();
                    this.favorite.protocolProperties =
                        Serialize.DeSerializeXML(serializedProperties, propertiesType) as ProtocolOptions;
                }
            }

            internal void LoadImageFromDatabase()
            {
                try
                {
                    this.TryLoadImageFromDatabase();
                }
                catch (Exception exception)
                {
                    Logging.Log.Error("Couldn't load image from database", exception);
                    // as recovery, load default icon
                    this.favorite.toolBarIcon = FavoriteIcons.GetFavoriteIcon(this.favorite);
                }
            }

            private void TryLoadImageFromDatabase()
            {
                using (Database database = DatabaseConnections.CreateInstance())
                {
                    byte[] imageData = database.GetFavoriteIcon(this.favorite.Id);
                    this.favorite.toolBarIcon = FavoriteIcons.LoadImage(imageData, this.favorite);
                }
            }

            /// <summary>
            /// Releases cached lazy loaded properties to force refresh by next access
            /// </summary>
            internal void ReleaseLoadedDetails()
            {
                // release protocolProperties, icon, other detail properties, not loaded with Entity
                this.protocolPropertiesLoaded = false;
                // don't dispose, because there is may be shared default protocol icon, which is still in use
                this.favorite.toolBarIcon = null;
                this.ReleaseReferences();
            }

            private void ReleaseReferences()
            {
                this.Security = null;
                this.Display = null;
                this.ExecuteBeforeConnect = null;
            }

            internal void UpdateFrom(FavoriteDetails source)
            {
                this.Security.UpdateFrom(source.Security);
                this.Display.UpdateFrom(source.Display);
                this.ExecuteBeforeConnect.UpdateFrom(source.ExecuteBeforeConnect);
            }
        }
    }
}
